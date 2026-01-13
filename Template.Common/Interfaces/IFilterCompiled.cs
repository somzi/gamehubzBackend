using System.Linq.Expressions;

namespace Template.Common.Interfaces
{
    public interface IFilterCompiled<T>
    {
        Expression<Func<T, bool>> Expression { get; }
    }
}
