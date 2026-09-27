using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>
/// Rejects queries the database would evaluate on ciphertext: values are encrypted with a random nonce, so comparing,
/// searching, sorting or grouping encrypted columns silently returns wrong results. Only null checks are allowed.
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

        if (!properties.IsEmpty)
            new Visitor(properties).Visit(queryExpression);

        return queryExpression;
    }

    private sealed class EncryptedProperties
    {
        private readonly Dictionary<Type, Dictionary<string, IReadOnlyProperty>> _byType = [];

        public bool IsEmpty => _byType.Count == 0;

        public static EncryptedProperties Create(IModel model)
        {
            var result = new EncryptedProperties();

            foreach (var entityType in model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetValueConverter() is not IEncryptionConverter)
                    continue;

                if (!result._byType.TryGetValue(entityType.ClrType, out var byName))
                    result._byType[entityType.ClrType] = byName = [];

                byName[property.Name] = property;
            }

            return result;
        }

        public IReadOnlyProperty? Find(Type type, string name)
            => _byType.TryGetValue(type, out var byName) && byName.TryGetValue(name, out var property) ? property : null;
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
                    throw new EntityFrameworkEncryptionException(
                        $"{property.DeclaringType.DisplayName()}.{property.Name} is encrypted and can't be compared, searched, " +
                        "sorted or grouped in a query: the database only sees random ciphertext, so the result would be wrong. " +
                        "Only '== null' and '!= null' are supported; filter by other columns and check the value in memory");
            }
        }

        private IReadOnlyProperty? Find(Expression expression)
        {
            expression = StripConvert(expression);

            return expression switch
            {
                MemberExpression { Expression: { } instance } member => Find(instance, member.Member.Name),
                MethodCallExpression call when IsEfProperty(call) && call.Arguments[1] is ConstantExpression { Value: string name }
                    => Find(call.Arguments[0], name),
                _ => null
            };
        }

        // generic code may access the property through an interface or base class cast
        private IReadOnlyProperty? Find(Expression instance, string name)
            => properties.Find(instance.Type, name)
               ?? (instance is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast
                   ? properties.Find(cast.Operand.Type, name)
                   : null);

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
