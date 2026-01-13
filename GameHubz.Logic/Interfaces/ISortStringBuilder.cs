namespace GameHubz.Logic.Interfaces
{
    public interface ISortStringBuilder
    {
        string CreateSortString(IList<SortItem>? sortItemRequests);
    }
}
