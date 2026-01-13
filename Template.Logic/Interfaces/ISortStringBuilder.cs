namespace Template.Logic.Interfaces
{
    public interface ISortStringBuilder
    {
        string CreateSortString(IList<SortItem>? sortItemRequests);
    }
}