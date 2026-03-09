namespace GameHubz.Logic.Interfaces
{
    public interface IHubActivityRepository : IRepository<HubActivityEntity>
    {
        Task<List<DashboardActivityDto>> GetRecentActivity(List<Guid> hubIds, int count);

        Task<EntityListDto<DashboardActivityDto>> GetRecentActivityPaged(List<Guid> hubIds, int pageNumber, int pageSize);
    }
}