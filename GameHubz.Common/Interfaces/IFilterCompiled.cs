using System.Linq.Expressions;

namespace GameHubz.Common.Interfaces
{
    public interface IFilterCompiled<T>
    {
        Expression<Func<T, bool>> Expression { get; }
    }
}
