using System.Linq.Expressions;
using Template.Common.Interfaces;

namespace Template.DataModels
{
    public class FilterCompiled<T> : IFilterCompiled<T>
    {
        public FilterCompiled(Expression<Func<T, bool>> expression)
        {
            this.Expression = expression;
        }

        public Expression<Func<T, bool>> Expression { get; }
    }
}