using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Template.Logic.Utility
{
    public class FilterExpressionBuilder : IFilterExpressionBuilder
    {
        public IFilterCompiled<T> CompileFilter<T>(IList<FilterItem>? filterItemList)
        {
            ParameterExpression parameter = Expression.Parameter(typeof(T), "t");

            if (filterItemList == null || filterItemList.Count == 0)
            {
                Expression<Func<T, bool>> exp = (T t) => true;
                return new FilterCompiled<T>(exp);
            }

            Expression? expression = null;

            if (filterItemList.Count == 1)
            {
                expression = this.GetExpression(parameter, filterItemList[0]);
            }
            else if (filterItemList.Count == 2)
            {
                expression = this.GetExpression(parameter, filterItemList[0], filterItemList[1]);
            }
            else
            {
                while (filterItemList.Count > 0)
                {
                    FilterItem filterOne = filterItemList[0];
                    FilterItem filterTwo = filterItemList[1];

                    expression = (expression == null)
                        ? this.GetExpression(parameter, filterItemList[0], filterItemList[1])
                        : this.CallAndOrExpression(filterItemList[0].LogicalOperator)(expression, this.GetExpression(parameter, filterItemList[0], filterItemList[1]));

                    filterItemList.Remove(filterOne);
                    filterItemList.Remove(filterTwo);

                    if (filterItemList.Count == 1)
                    {
                        var newExpression = this.GetExpression(parameter, filterItemList[0]);

                        if (newExpression == null)
                        {
                            throw new CoreException("Expression could not be created.");
                        }

                        expression = this.CallAndOrExpression(filterItemList[0].LogicalOperator)(expression, newExpression);
                        filterItemList.RemoveAt(0);
                    }
                }
            }

            if (expression == null)
            {
                throw new CoreException("Expression was not created.");
            }

            return new FilterCompiled<T>(Expression.Lambda<Func<T, bool>>(expression!, parameter));
        }

#pragma warning disable CA1822 // Mark members as static

        private Func<Expression, Expression, BinaryExpression> CallAndOrExpression(FilterLogicalOperator logicalOperator)
#pragma warning restore CA1822 // Mark members as static
            => logicalOperator switch
            {
                FilterLogicalOperator.AND => Expression.AndAlso,
                FilterLogicalOperator.OR => Expression.Or,
                _ => throw new CoreException("Unregistered logicalOperator.")
            };

#pragma warning disable CA1822 // Mark members as static

        private Expression? GetExpression(ParameterExpression param, FilterItem filterItem)
#pragma warning restore CA1822 // Mark members as static
        {
            var dateTimeValue = "Value";
            var dateTimeDate = "Date";

            MemberExpression member = Expression.Property(param, filterItem.PropertyName);

            var propertyType = ((PropertyInfo)member.Member).PropertyType;
            var converter = TypeDescriptor.GetConverter(propertyType);

            if (!converter.CanConvertFrom(typeof(string)))
            {
                throw new NotSupportedException();
            }

            var propertyValue = converter.ConvertFromInvariantString(filterItem.Value);
            var constant = Expression.Constant(propertyValue);

            Expression valueExpression = Expression.Convert(constant, propertyType);

            if (member.Type == typeof(DateTime))
            {
                member = Expression.Property(member, dateTimeDate);
                valueExpression = Expression.Property(valueExpression, dateTimeDate);
            }

            if (member.Type == typeof(DateTime?))
            {
                member = Expression.Property(Expression.Property(member, dateTimeValue), dateTimeDate);
                valueExpression = Expression.Property(Expression.Property(valueExpression, dateTimeValue), dateTimeDate);
            }

            return filterItem.ExpressionOperator switch
            {
                ExpressionOperator.Equals => Expression.Equal(member, valueExpression),
                ExpressionOperator.Greater => Expression.GreaterThan(member, valueExpression),
                ExpressionOperator.GreaterOrEquals => Expression.GreaterThanOrEqual(member, valueExpression),
                ExpressionOperator.Less => Expression.LessThan(member, valueExpression),
                ExpressionOperator.LessOrEqual => Expression.LessThanOrEqual(member, valueExpression),
                ExpressionOperator.Contains => Expression.Call(member, typeof(string).GetMethod("Contains", new Type[] { typeof(string) })!, valueExpression),
                ExpressionOperator.StartsWith => Expression.Call(member, typeof(string).GetMethod("StartsWith", new Type[] { typeof(string) })!, valueExpression),
                ExpressionOperator.Different => Expression.NotEqual(member, valueExpression),
                _ => null,
            };
        }

        private BinaryExpression GetExpression(ParameterExpression param, FilterItem filterItem1, FilterItem filterItem2)
        {
            Expression? bin1 = this.GetExpression(param, filterItem1);
            Expression? bin2 = this.GetExpression(param, filterItem2);

            var expression = this.CallAndOrExpression(filterItem1.LogicalOperator);

            return expression(bin1!, bin2!);
        }
    }
}