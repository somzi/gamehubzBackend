namespace GameHubz.Logic.Interfaces
{
    public interface IFilterExpressionBuilder
    {
        IFilterCompiled<T> CompileFilter<T>(IList<FilterItem>? filterItemList);
    }
}
