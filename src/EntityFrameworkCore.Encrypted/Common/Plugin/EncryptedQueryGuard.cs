using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>
/// Rejects queries the database would evaluate on ciphertext: values are encrypted with a random nonce, so comparing,
/// searching, sorting or grouping encrypted columns silently returns wrong results. Only null checks are allowed,
/// and equality with values for properties with a blind index, which are rewritten to compare the blind index.
/// </summary>
/// <remarks>Runs once per query shape: compiled queries are cached by EF.</remarks>
internal sealed class EncryptedQueryGuard : IQueryExpressionInterceptor
{
    private static readonly ConditionalWeakTable<IModel, EncryptedProperties> Cache = new();

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (eventData.Context is not { } context)
            return queryExpression;

        var properties = Cache.GetValue(context.Model, EncryptedProperties.Create);

        if (properties.IsEmpty)
            return queryExpression;

        if (properties.HasBlindIndexes)
            queryExpression = new BlindIndexRewriter(properties).Visit(queryExpression);

        new Visitor(properties).Visit(queryExpression);
        return queryExpression;
    }

    /// <summary>
    /// Encrypted properties resolved through the metadata path of an expression, so occurrences of the same CLR type
    /// (owned types, complex types used by several properties) are distinguished: x.Billing.Street vs x.Shipping.Street.
    /// </summary>
    private sealed class EncryptedProperties
    {
        // CLR type -> entity, owned and complex types mapped to it
        private readonly Dictionary<Type, List<IReadOnlyTypeBase>> _types = [];

        public bool IsEmpty { get; private set; } = true;

        public bool HasBlindIndexes { get; private set; }

        public static EncryptedProperties Create(IModel model)
        {
            var result = new EncryptedProperties();

            foreach (var entityType in model.GetEntityTypes())
                result.Add(entityType);

            return result;
        }

        private void Add(IReadOnlyTypeBase type)
        {
            if (!_types.TryGetValue(type.ClrType, out var types))
                _types[type.ClrType] = types = [];

            types.Add(type);
            IsEmpty &= !type.GetDeclaredProperties().Any(IsEncrypted);
            HasBlindIndexes |= type.GetDeclaredProperties().Any(x => x.GetValueConverter() is IBlindIndexConverter);

            foreach (var complexProperty in type.GetDeclaredComplexProperties())
                Add(complexProperty.ComplexType);
        }

        public IReadOnlyProperty? Find(Expression instance, string name)
        {
            if (Resolve(instance) is { } type)
                return type.FindProperty(name) is { } property && IsEncrypted(property) ? property : null;

            // not traceable to the model (e.g. a lambda parameter of a complex type): encrypted only if it is in every occurrence
            var candidates = Candidates(instance).Select(x => x.FindProperty(name)).ToList();

            return candidates.Count > 0 && candidates.All(x => x != null && IsEncrypted(x)) ? candidates[0] : null;
        }

        // entity of a parameter; complex type or owned entity type a member leads to
        private IReadOnlyTypeBase? Resolve(Expression expression)
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast)
                return Single(cast.Type) ?? Resolve(cast.Operand);

            if (expression is MemberExpression { Expression: { } parentExpression } member && Resolve(parentExpression) is { } parent)
            {
                return (IReadOnlyTypeBase?)parent.FindComplexProperty(member.Member.Name)?.ComplexType
                       ?? (parent as IReadOnlyEntityType)?.FindNavigation(member.Member.Name)?.TargetEntityType;
            }

            return Single(expression.Type);
        }

        private IReadOnlyTypeBase? Single(Type type)
            => _types.TryGetValue(type, out var types) && types.Count == 1 ? types[0] : null;

        private IEnumerable<IReadOnlyTypeBase> Candidates(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast)
            {
                if (_types.TryGetValue(cast.Type, out var castTypes))
                    return castTypes;

                expression = cast.Operand;
            }

            return _types.GetValueOrDefault(expression.Type) ?? [];
        }

        private static bool IsEncrypted(IReadOnlyProperty property)
            => property.GetValueConverter() is IEncryptionConverter;
    }

    /// <summary>
    /// Compares the blind index instead of the encrypted value: <c>x.Email == email</c> becomes
    /// <c>EF.Property(x, "Email_Index") == email</c>, and EF hashes the parameter with the converter of the index.
    /// Only comparisons with parameters and constants: the hash of another column can't be computed in the database.
    /// </summary>
    private sealed class BlindIndexRewriter(EncryptedProperties properties) : ExpressionVisitor
    {
        private static readonly MethodInfo PropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                if (IsValue(node.Right) && ToIndex(node.Left) is { } left)
                    return node.Update(left, node.Conversion, Visit(node.Right));

                if (IsValue(node.Left) && ToIndex(node.Right) is { } right)
                    return node.Update(Visit(node.Left), node.Conversion, right);
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // values.Contains(x.Email), Enumerable.Contains(values, x.Email)
            if (node.Method.Name == nameof(Enumerable.Contains))
            {
                if (node.Object != null && node.Arguments.Count == 1 && IsValue(node.Object) && ToIndex(node.Arguments[0]) is { } item)
                    return node.Update(node.Object, [item]);

                if (node.Object == null && node.Arguments.Count == 2
                    && node.Method.DeclaringType == typeof(Enumerable)
                    && IsValue(node.Arguments[0]) && ToIndex(node.Arguments[1]) is { } argument)
                    return node.Update(null, [node.Arguments[0], argument]);
            }

            if (node.Method.Name == nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                && node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Arguments is [var source, NewArrayExpression setters])
            {
                return node.Update(null, [Visit(source), setters.Update(setters.Expressions.SelectMany(AddIndexSetter))]);
            }

            return base.VisitMethodCall(node);
        }

        // SetProperty(x => x.Email, value) also sets the blind index to the same value
        private IEnumerable<Expression> AddIndexSetter(Expression setter)
        {
            yield return setter;

            if (setter is not NewExpression { Arguments: [LambdaExpression selector, var value] } tuple
                || ToIndex(selector.Body) is not { } index)
                yield break;

            // null clears both columns: the converter isn't applied to nulls
            if (!IsValue(value) && !IsNullValue(value))
                throw new EntityFrameworkEncryptionException(
                    $"{EncryptionConvention.DisplayName(Find(selector.Body)!)} has a blind index and can only be set to a value in ExecuteUpdate, " +
                    "not to an expression: its hash can't be computed in the database");

            yield return tuple.Update([Expression.Lambda(selector.Type, index, selector.Parameters), value]);
        }

        /// <summary>Same access to the blind index shadow property, or <c>null</c> if the expression isn't a property with one.</summary>
        private Expression? ToIndex(Expression expression)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                    return ToIndex(convert.Operand) is { } operand ? convert.Update(operand) : null;

                case MemberExpression { Expression: { } instance } member
                    when Find(member) is { } property && BlindIndex.Find(property) is { } index:
                    return Expression.Call(PropertyMethod.MakeGenericMethod(member.Type), Visit(instance), Expression.Constant(index.Name));

                case MethodCallExpression { Arguments: [var instance, ConstantExpression { Value: string }] } call
                    when call.Method.DeclaringType == typeof(EF) && call.Method.Name == nameof(EF.Property)
                         && Find(call) is { } property && BlindIndex.Find(property) is { } index:
                    return call.Update(null, [Visit(instance), Expression.Constant(index.Name)]);

                default:
                    return null;
            }
        }

        private IReadOnlyProperty? Find(Expression expression)
            => expression switch
            {
                MemberExpression { Expression: { } instance } member => properties.Find(instance, member.Member.Name),
                MethodCallExpression { Arguments: [var instance, ConstantExpression { Value: string name }] } => properties.Find(instance, name),
                _ => null
            };

        // query parameters and constants: hashed by EF with the converter of the index; null checks stay on the column
        private static bool IsValue(Expression expression)
            => StripConvert(expression) is QueryParameterExpression or ConstantExpression { Value: not null and not IQueryable };

        private static bool IsNullValue(Expression expression)
            => StripConvert(expression) is ConstantExpression { Value: null } or DefaultExpression;

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
                expression = unary.Operand;

            return expression;
        }
    }

    private sealed class Visitor(EncryptedProperties properties) : ExpressionVisitor
    {
        // lambda arguments the database evaluates as keys: ordering, grouping, joining, min/max
        private static readonly Dictionary<string, int[]> KeySelectors = new()
        {
            [nameof(Queryable.OrderBy)] = [1],
            [nameof(Queryable.OrderByDescending)] = [1],
            [nameof(Queryable.ThenBy)] = [1],
            [nameof(Queryable.ThenByDescending)] = [1],
            [nameof(Queryable.GroupBy)] = [1],
            [nameof(Queryable.DistinctBy)] = [1],
            [nameof(Queryable.MinBy)] = [1],
            [nameof(Queryable.MaxBy)] = [1],
            [nameof(Queryable.Min)] = [1],
            [nameof(Queryable.Max)] = [1],
            [nameof(Queryable.Join)] = [2, 3],
            [nameof(Queryable.GroupJoin)] = [2, 3]
        };

        // operators the database evaluates by comparing the elements of the sequence: Select(x => x.Secret).Distinct()
        private static readonly HashSet<string> ElementComparisons =
        [
            nameof(Queryable.Distinct), nameof(Queryable.Min), nameof(Queryable.Max), nameof(Queryable.Contains),
            nameof(Queryable.Order), nameof(Queryable.OrderDescending), nameof(Queryable.Union),
            nameof(Queryable.Intersect), nameof(Queryable.Except), nameof(Queryable.SequenceEqual)
        ];

        // operators returning their source elements unchanged
        private static readonly HashSet<string> PassThrough =
        [
            nameof(Queryable.Where), nameof(Queryable.Take), nameof(Queryable.Skip), nameof(Queryable.TakeWhile),
            nameof(Queryable.SkipWhile), nameof(Queryable.Concat), nameof(Queryable.Reverse), nameof(Queryable.AsQueryable),
            nameof(Enumerable.AsEnumerable), nameof(EntityFrameworkQueryableExtensions.AsNoTracking),
            nameof(EntityFrameworkQueryableExtensions.AsTracking), nameof(EntityFrameworkQueryableExtensions.TagWith)
        ];

        // lambda parameters bound to encrypted values: Select(x => x.Secret).Where(s => s == "...")
        private readonly Dictionary<ParameterExpression, IReadOnlyProperty> _encryptedParameters = [];

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                // "x.Secret == null" is translated to "IS NULL", which works on ciphertext
                if (!IsNull(node.Left) && !IsNull(node.Right))
                    Reject(node.Left, node.Right);
            }
            else if (node.NodeType is ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                     or ExpressionType.LessThan or ExpressionType.LessThanOrEqual)
            {
                Reject(node.Left, node.Right);
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // EF.Property(x, "Secret") itself is a plain read
            if (IsEfProperty(node))
                return base.VisitMethodCall(node);

            // x.Secret.Contains(...), x.Secret.ToLower(), ...
            if (node.Object != null)
                Reject(node.Object);

            // string.IsNullOrEmpty(x.Secret), list.Contains(x.Secret), EF.Functions.Like(x.Secret, ...), ...
            if (IsTranslatedByDatabase(node))
                Reject([..node.Arguments]);

            if (IsSequenceOperator(node))
                CheckSequenceOperator(node);

            if ((node.Method.DeclaringType == typeof(Queryable) || node.Method.DeclaringType == typeof(Enumerable))
                && KeySelectors.TryGetValue(node.Method.Name, out var indexes))
            {
                foreach (var index in indexes.Where(x => x < node.Arguments.Count))
                {
                    if (StripQuote(node.Arguments[index]) is LambdaExpression selector)
                        Reject(KeyParts(selector.Body));
                }
            }

            return base.VisitMethodCall(node);
        }

        // x.Secret.Length is evaluated on ciphertext as well
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null)
                Reject(node.Expression);

            return base.VisitMember(node);
        }

        // x.Blob.Length
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.ArrayLength)
                Reject(node.Operand);

            return base.VisitUnary(node);
        }

        private bool IsTranslatedByDatabase(MethodCallExpression node)
        {
            var type = node.Method.DeclaringType;

            return type == typeof(string)
                   || type == typeof(object)
                   || type == typeof(Enumerable)
                   || type == typeof(Queryable)
                   || type == typeof(MemoryExtensions)
                   || node.Method.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(DbFunctions)
                   || node.Object != null && node.Object.Type != typeof(string) && node.Method.Name == "Contains";
        }

        private void Reject(params Expression[] expressions)
        {
            foreach (var expression in expressions)
            {
                if (Find(expression) is { } property)
                    throw Rejected(property);
            }
        }

        private static EntityFrameworkEncryptionException Rejected(IReadOnlyProperty property)
            => new($"{EncryptionConvention.DisplayName(property)} is encrypted and can't be compared, searched, " +
                   "sorted or grouped in a query: the database only sees random ciphertext, so the result would be wrong. " +
                   "Only '== null' and '!= null' are supported; filter by other columns and check the value in memory");

        private void CheckSequenceOperator(MethodCallExpression node)
        {
            // every sequence operand: plain.Union(q.Select(x => x.Secret)), outer.Join(q.Select(x => x.Secret), ...)
            var sequences = node.Arguments.Select(FindSequence).ToArray();

            // Contains(source, item): only the source is a sequence of elements being compared
            var compared = node.Method.Name is nameof(Queryable.Contains) ? sequences.Take(1) : sequences;

            if (ElementComparisons.Contains(node.Method.Name)
                && node.Arguments.Skip(1).All(x => StripQuote(x) is not LambdaExpression)
                && compared.FirstOrDefault(x => x != null) is { } sequence)
            {
                throw Rejected(sequence.Property);
            }

            // the elements are the encrypted values themselves: lambdas over them are checked like the property
            if (node.Method.Name is nameof(Queryable.Join) or nameof(Queryable.GroupJoin) && node.Arguments.Count >= 5)
            {
                Bind(node.Arguments[2], 0, sequences[0]);
                Bind(node.Arguments[3], 0, sequences[1]);
                Bind(node.Arguments[4], 0, sequences[0]);
                Bind(node.Arguments[4], 1, sequences[1]);
            }
            else if (node.Method.Name is nameof(Queryable.Zip) && node.Arguments.Count >= 3)
            {
                Bind(node.Arguments[2], 0, sequences[0]);
                Bind(node.Arguments[2], 1, sequences[1]);
            }
            else
            {
                foreach (var lambda in node.Arguments.Skip(1))
                    Bind(lambda, 0, sequences[0]);
            }
        }

        private void Bind(Expression argument, int parameterIndex, (IReadOnlyProperty Property, bool IsValue)? sequence)
        {
            if (sequence is { IsValue: true } value
                && StripQuote(argument) is LambdaExpression lambda
                && parameterIndex < lambda.Parameters.Count
                && lambda.Parameters[parameterIndex].Type == value.Property.ClrType)
            {
                _encryptedParameters[lambda.Parameters[parameterIndex]] = value.Property;
            }
        }

        /// <summary>Encrypted property whose values the sequence contains, as is or within projected objects.</summary>
        private (IReadOnlyProperty Property, bool IsValue)? FindSequence(Expression expression)
        {
            if (StripConvert(expression) is not MethodCallExpression call || !IsSequenceOperator(call))
                return null;

            if (call.Method.Name is nameof(Queryable.Select) && StripQuote(call.Arguments[1]) is LambdaExpression selector)
            {
                // nested projections: q.Select(x => x.Secret).Select(s => s)
                Bind(selector, 0, FindSequence(call.Arguments[0]));

                if (Find(selector.Body) is { } value)
                    return (value, true);

                return KeyParts(selector.Body).Select(Find).FirstOrDefault(x => x != null) is { } part ? (part, false) : null;
            }

            if (!PassThrough.Contains(call.Method.Name))
                return null;

            return call.Method.Name is nameof(Queryable.Concat)
                ? FindSequence(call.Arguments[0]) ?? FindSequence(call.Arguments[1])
                : FindSequence(call.Arguments[0]);
        }

        private static bool IsSequenceOperator(MethodCallExpression node)
            => node.Arguments.Count > 0
               && (node.Method.DeclaringType == typeof(Queryable)
                   || node.Method.DeclaringType == typeof(Enumerable)
                   || node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions));

        private IReadOnlyProperty? Find(Expression expression)
        {
            expression = StripConvert(expression);

            return expression switch
            {
                MemberExpression { Expression: { } instance } member => Find(instance, member.Member.Name),
                MethodCallExpression call when IsEfProperty(call) && call.Arguments[1] is ConstantExpression { Value: string name }
                    => Find(call.Arguments[0], name),
                ParameterExpression parameter => _encryptedParameters.GetValueOrDefault(parameter),
                _ => null
            };
        }

        // generic code may access the property through an interface or base class cast
        private IReadOnlyProperty? Find(Expression instance, string name)
            => properties.Find(instance, name);

        private static Expression[] KeyParts(Expression body)
            => StripConvert(body) switch
            {
                NewExpression x => [..x.Arguments],
                MemberInitExpression x => [..x.Bindings.OfType<MemberAssignment>().Select(b => b.Expression)],
                var x => [x]
            };

        private static bool IsEfProperty(MethodCallExpression node)
            => node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property);

        private static bool IsNull(Expression expression)
            => StripConvert(expression) is ConstantExpression { Value: null } or DefaultExpression;

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
                expression = unary.Operand;

            return expression;
        }

        private static Expression StripQuote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;
    }
}
