namespace GameHubz.Logic.Interfaces
{
    public interface IHubActivityRepository : IRepository<HubActivityEntity>
    {
        Task<List<DashboardActivityDto>> GetRecentActivity(List<Guid> hubIds, int count);
    }
}