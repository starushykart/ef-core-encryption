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

        EncryptedModel.EnsureBuiltWithEncryption(context);
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

    /// <summary>What an expression holds, as far as encrypted values go.</summary>
    private abstract record Shape;

    /// <summary>An encrypted value, or a value computed from one in the database (coalesce, concatenation, conditional).</summary>
    private sealed record EncryptedValue(IReadOnlyProperty Property) : Shape;

    /// <summary>A projected object (anonymous type, DTO, transparent identifier) with members holding encrypted values.</summary>
    private sealed record Projection(IReadOnlyDictionary<string, Shape> Members) : Shape;

    /// <summary>A group: its key and its elements.</summary>
    private sealed record Grouping(Shape? Key, Shape? Element) : Shape;

    /// <summary>
    /// Tracks encrypted values through the query: columns, values computed from them, projections, subqueries and groups,
    /// with lambda parameters bound to the elements of the sequence they iterate. Rejects comparisons, database functions,
    /// key selectors and element comparisons involving encrypted values, and ExecuteUpdate setters copying ciphertext.
    /// </summary>
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

        // operators returning their source elements
        private static readonly HashSet<string> PassThrough =
        [
            nameof(Queryable.Where), nameof(Queryable.Take), nameof(Queryable.Skip), nameof(Queryable.TakeWhile),
            nameof(Queryable.SkipWhile), nameof(Queryable.Reverse), nameof(Queryable.AsQueryable), nameof(Enumerable.AsEnumerable),
            nameof(Queryable.OrderBy), nameof(Queryable.OrderByDescending), nameof(Queryable.ThenBy), nameof(Queryable.ThenByDescending),
            nameof(Queryable.Order), nameof(Queryable.OrderDescending), nameof(Queryable.Distinct), nameof(Queryable.DistinctBy),
            nameof(Queryable.DefaultIfEmpty), nameof(Queryable.Cast), nameof(Queryable.OfType),
            nameof(EntityFrameworkQueryableExtensions.AsNoTracking), nameof(EntityFrameworkQueryableExtensions.AsTracking),
            nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution), nameof(EntityFrameworkQueryableExtensions.TagWith),
            nameof(EntityFrameworkQueryableExtensions.Include), nameof(EntityFrameworkQueryableExtensions.ThenInclude),
            nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters), nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes),
            nameof(RelationalQueryableExtensions.AsSplitQuery), nameof(RelationalQueryableExtensions.AsSingleQuery)
        ];

        // sequence operators returning one of the elements
        private static readonly HashSet<string> SingleElement =
        [
            nameof(Queryable.First), nameof(Queryable.FirstOrDefault), nameof(Queryable.Single), nameof(Queryable.SingleOrDefault),
            nameof(Queryable.Last), nameof(Queryable.LastOrDefault), nameof(Queryable.ElementAt), nameof(Queryable.ElementAtOrDefault),
            nameof(Queryable.Min), nameof(Queryable.Max), nameof(Queryable.MinBy), nameof(Queryable.MaxBy)
        ];

        // set operators: elements come from both sequences
        private static readonly HashSet<string> Combining =
        [
            nameof(Queryable.Concat), nameof(Queryable.Union), nameof(Queryable.Intersect), nameof(Queryable.Except)
        ];

        // lambda parameters bound to what the elements of their sequence hold: Select(x => x.Secret).Where(s => s == "...")
        private readonly Dictionary<ParameterExpression, Shape> _parameters = [];

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

            if (IsExecuteUpdate(node, out var setters))
                CheckSetters(setters);

            // x.Secret.Contains(...), x.Secret.ToLower(), ...
            if (node.Object != null)
                RejectValue(node.Object);

            // string.IsNullOrEmpty(x.Secret), list.Contains(x.Secret), EF.Functions.Like(x.Secret, ...), ...;
            // sequences are checked by the operators comparing their elements: g.Count() only counts
            if (IsTranslatedByDatabase(node))
                Reject([..node.Arguments.Where((_, i) => !IsSequence(node.Method.GetParameters()[i].ParameterType))]);

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
                RejectValue(node.Expression);

            return base.VisitMember(node);
        }

        // x.Blob.Length
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.ArrayLength)
                RejectValue(node.Operand);

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

        // SetProperty(x => x.Plain, x => x.Secret) would copy ciphertext; SetProperty(x => x.Secret, x => x.Other)
        // would store a value the database can't encrypt
        private void CheckSetters(NewArrayExpression setters)
        {
            foreach (var setter in setters.Expressions)
            {
                if (setter is not NewExpression { Arguments: [LambdaExpression selector, var value] }
                    || StripQuote(value) is not LambdaExpression valueSelector
                    || !ReadsParameters(valueSelector))
                    continue;

                if (ShapeOf(selector.Body) is EncryptedValue target)
                    throw new EntityFrameworkEncryptionException(
                        $"{EncryptionConvention.DisplayName(target.Property)} is encrypted and can only be set to a value in ExecuteUpdate, " +
                        "not to an expression over columns: the database can't encrypt it");

                if (Encrypted(ShapeOf(valueSelector.Body)) is { } source)
                    throw new EntityFrameworkEncryptionException(
                        $"{EncryptionConvention.DisplayName(source)} is encrypted and can't be copied to another column in ExecuteUpdate: " +
                        "the database would copy its ciphertext. Load the entities and use SaveChanges instead");
            }
        }

        // a value or a projected object holding an encrypted value
        private void Reject(params Expression[] expressions)
        {
            foreach (var expression in expressions)
            {
                if (Encrypted(ShapeOf(expression)) is { } property)
                    throw Rejected(property);
            }
        }

        // an encrypted value itself: members of projected objects are only read
        private void RejectValue(Expression expression)
        {
            if (ShapeOf(expression) is EncryptedValue value)
                throw Rejected(value.Property);
        }

        private static EntityFrameworkEncryptionException Rejected(IReadOnlyProperty property)
            => new($"{EncryptionConvention.DisplayName(property)} is encrypted and can't be compared, searched, " +
                   "sorted or grouped in a query: the database only sees random ciphertext, so the result would be wrong. " +
                   "Only '== null' and '!= null' are supported; filter by other columns and check the value in memory");

        private void CheckSequenceOperator(MethodCallExpression node)
        {
            // every sequence operand: plain.Union(q.Select(x => x.Secret)), outer.Join(q.Select(x => x.Secret), ...)
            var sequences = node.Arguments.Select(ElementShape).ToArray();

            // Contains(source, item): only the source is a sequence of elements being compared
            var compared = node.Method.Name is nameof(Queryable.Contains) ? sequences.Take(1) : sequences;

            if (ElementComparisons.Contains(node.Method.Name)
                && node.Arguments.Skip(1).All(x => StripQuote(x) is not LambdaExpression)
                && compared.Select(Encrypted).FirstOrDefault(x => x != null) is { } property)
            {
                throw Rejected(property);
            }

            BindLambdas(node, sequences);
        }

        /// <summary>Binds the lambda parameters of a sequence operator to what the elements they receive hold.</summary>
        private void BindLambdas(MethodCallExpression node, Shape?[] sequences)
        {
            var arguments = node.Arguments;

            switch (node.Method.Name)
            {
                case nameof(Queryable.Join) when arguments.Count >= 5:
                    Bind(arguments[2], sequences[0]);
                    Bind(arguments[3], sequences[1]);
                    Bind(arguments[4], sequences[0], sequences[1]);
                    break;

                case nameof(Queryable.GroupJoin) when arguments.Count >= 5:
                    Bind(arguments[2], sequences[0]);
                    Bind(arguments[3], sequences[1]);
                    Bind(arguments[4], sequences[0], sequences[1] == null ? null : new Grouping(null, sequences[1]));
                    break;

                case nameof(Queryable.Zip) when arguments.Count >= 3:
                    Bind(arguments[2], sequences[0], sequences[1]);
                    break;

                case nameof(Queryable.GroupBy):
                    _ = GroupBy(node, sequences[0]);
                    break;

                case nameof(Queryable.SelectMany):
                    _ = SelectMany(node, sequences[0]);
                    break;

                default:
                    foreach (var lambda in arguments.Skip(1))
                        Bind(lambda, sequences[0]);
                    break;
            }
        }

        private void Bind(Expression argument, params Shape?[] shapes)
        {
            if (StripQuote(argument) is not LambdaExpression lambda)
                return;

            for (var i = 0; i < Math.Min(shapes.Length, lambda.Parameters.Count); i++)
            {
                if (shapes[i] is { } shape)
                    _parameters[lambda.Parameters[i]] = shape;
            }
        }

        /// <summary>What the elements of a sequence hold, or <c>null</c> if no encrypted values.</summary>
        private Shape? ElementShape(Expression expression)
        {
            expression = StripConvert(expression);

            // the elements of a group: GroupBy(x => x.Id, x => x.Secret).Select(g => g.Max())
            if (expression is ParameterExpression parameter)
                return _parameters.GetValueOrDefault(parameter) is Grouping group ? group.Element : null;

            if (expression is not MethodCallExpression call || !IsSequenceOperator(call))
                return null;

            var source = ElementShape(call.Arguments[0]);

            switch (call.Method.Name)
            {
                case nameof(Queryable.Select) when StripQuote(call.Arguments[1]) is LambdaExpression selector:
                    Bind(selector, source);
                    return ShapeOf(selector.Body);

                case nameof(Queryable.SelectMany):
                    return SelectMany(call, source);

                case nameof(Queryable.GroupBy):
                    return GroupBy(call, source);

                case nameof(Queryable.Join) or nameof(Queryable.GroupJoin) or nameof(Queryable.Zip)
                    when StripQuote(call.Arguments[^1]) is LambdaExpression result && call.Arguments.Count >= 3:
                    BindLambdas(call, [..call.Arguments.Select(ElementShape)]);
                    return ShapeOf(result.Body);

                case var name when Combining.Contains(name):
                    return source ?? ElementShape(call.Arguments[1]);

                case var name when PassThrough.Contains(name):
                    return source;

                default:
                    return null;
            }
        }

        private Shape? SelectMany(MethodCallExpression call, Shape? source)
        {
            if (StripQuote(call.Arguments[1]) is not LambdaExpression collectionSelector)
                return null;

            Bind(collectionSelector, source);
            var collection = ElementShape(collectionSelector.Body);

            if (call.Arguments.Count < 3 || StripQuote(call.Arguments[2]) is not LambdaExpression resultSelector)
                return collection;

            Bind(resultSelector, source, collection);
            return ShapeOf(resultSelector.Body);
        }

        private Shape? GroupBy(MethodCallExpression call, Shape? source)
        {
            var lambdas = call.Arguments.Skip(1).Select(StripQuote).OfType<LambdaExpression>().ToList();

            if (lambdas.Count == 0)
                return null;

            Bind(lambdas[0], source);
            var key = ShapeOf(lambdas[0].Body);

            // GroupBy(key, element), GroupBy(key, result: (key, elements)), GroupBy(key, element, result)
            var element = source;
            var next = 1;

            if (lambdas.Count > 1 && lambdas[1].Parameters.Count == 1)
            {
                Bind(lambdas[1], source);
                element = ShapeOf(lambdas[1].Body);
                next = 2;
            }

            var group = key == null && element == null ? null : new Grouping(key, element);

            if (lambdas.Count <= next)
                return group;

            Bind(lambdas[next], key, group);
            return ShapeOf(lambdas[next].Body);
        }

        /// <summary>What a value holds: an encrypted value, a projected object or a group.</summary>
        private Shape? ShapeOf(Expression expression)
        {
            expression = StripQuote(StripConvert(expression));

            switch (expression)
            {
                case ParameterExpression parameter:
                    return _parameters.GetValueOrDefault(parameter);

                case MemberExpression { Expression: { } instance } member:
                    return Member(instance, member.Member.Name);

                case MethodCallExpression call when IsEfProperty(call) && call.Arguments[1] is ConstantExpression { Value: string name }:
                    return Member(call.Arguments[0], name);

                // q.Where(...).Select(x => x.Secret).FirstOrDefault(), q.Max(x => x.Secret)
                case MethodCallExpression call when IsSequenceOperator(call) && SingleElement.Contains(call.Method.Name):
                    if (call.Arguments.Count == 2 && StripQuote(call.Arguments[1]) is LambdaExpression selector
                        && call.Method.Name is nameof(Queryable.Min) or nameof(Queryable.Max))
                    {
                        Bind(selector, ElementShape(call.Arguments[0]));
                        return ShapeOf(selector.Body);
                    }

                    return ElementShape(call.Arguments[0]);

                case MethodCallExpression call when call.Method.DeclaringType == typeof(string) && call.Method.Name == nameof(string.Concat):
                    return call.Arguments.Select(ShapeOf).OfType<EncryptedValue>().FirstOrDefault();

                // computed in the database from the encrypted value: x.Secret ?? "", x.Secret + "", c ? x.Secret : x.Plain
                case BinaryExpression binary when binary.NodeType is ExpressionType.Coalesce or ExpressionType.Add:
                    return ShapeOf(binary.Left) as EncryptedValue ?? ShapeOf(binary.Right) as EncryptedValue;

                case ConditionalExpression conditional:
                    return ShapeOf(conditional.IfTrue) as EncryptedValue ?? ShapeOf(conditional.IfFalse) as EncryptedValue;

                case NewExpression @new:
                    return Project(@new, []);

                case MemberInitExpression init:
                    return Project(init.NewExpression, init.Bindings.OfType<MemberAssignment>().Select(x => (x.Member.Name, x.Expression)));

                default:
                    return null;
            }
        }

        private Shape? Member(Expression instance, string name)
            => ShapeOf(instance) switch
            {
                Projection projection => projection.Members.GetValueOrDefault(name),
                Grouping group => name == nameof(IGrouping<,>.Key) ? group.Key : null,

                // generic code may access the property through an interface or base class cast
                _ => properties.Find(instance, name) is { } property ? new EncryptedValue(property) : null
            };

        // new { x.Id, x.Secret }, new Dto { Secret = x.Secret }, new Dto(x.Secret) for positional records
        private Projection? Project(NewExpression @new, IEnumerable<(string Name, Expression Value)> assignments)
        {
            var parameters = @new.Constructor?.GetParameters() ?? [];
            var members = new Dictionary<string, Shape>();

            var arguments = @new.Arguments.Select((x, i) => (Name: @new.Members?[i].Name ?? (i < parameters.Length ? parameters[i].Name : null), Value: x));

            foreach (var (name, value) in arguments.Concat(assignments.Select(x => ((string?)x.Name, x.Value))))
            {
                if (name != null && ShapeOf(value) is { } shape)
                    members[name] = shape;
            }

            return members.Count > 0 ? new Projection(members) : null;
        }

        /// <summary>Encrypted property held by a shape, also within projected objects and groups.</summary>
        private static IReadOnlyProperty? Encrypted(Shape? shape)
            => shape switch
            {
                EncryptedValue value => value.Property,
                Projection projection => projection.Members.Values.Select(Encrypted).FirstOrDefault(x => x != null),
                Grouping group => Encrypted(group.Key) ?? Encrypted(group.Element),
                _ => null
            };

        private static bool IsExecuteUpdate(MethodCallExpression node, out NewArrayExpression setters)
        {
            setters = null!;

            if (node.Method.Name != nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                || node.Method.DeclaringType != typeof(EntityFrameworkQueryableExtensions)
                || node.Arguments is not [_, NewArrayExpression array])
                return false;

            setters = array;
            return true;
        }

        private static bool ReadsParameters(LambdaExpression lambda)
        {
            var finder = new ParameterFinder(lambda.Parameters);
            finder.Visit(lambda.Body);
            return finder.Found;
        }

        private static bool IsSequence(Type type)
            => type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

        private static bool IsSequenceOperator(MethodCallExpression node)
            => node.Arguments.Count > 0
               && (node.Method.DeclaringType == typeof(Queryable)
                   || node.Method.DeclaringType == typeof(Enumerable)
                   || node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                   || node.Method.DeclaringType == typeof(RelationalQueryableExtensions));

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
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } unary)
                expression = unary.Operand;

            return expression;
        }

        private static Expression StripQuote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

        private sealed class ParameterFinder(IReadOnlyCollection<ParameterExpression> parameters) : ExpressionVisitor
        {
            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found |= parameters.Contains(node);
                return node;
            }
        }
    }
}
