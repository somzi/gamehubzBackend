namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubRepository : IRepository<UserHubEntity>
    {
        Task<List<Guid>> GetHubIdsByUserId(Guid userId);
    }
}