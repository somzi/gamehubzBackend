namespace Template.Logic.Interfaces
{
    public interface IFilterExpressionBuilder
    {
        IFilterCompiled<T> CompileFilter<T>(IList<FilterItem>? filterItemList);
    }
}