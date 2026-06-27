namespace GameHubz.Logic.Interfaces
{
    public interface IUserSocialRepository : IRepository<UserSocialEntity>
    {
        Task<List<UserSocialEntity>> GetByUserId(Guid userId);
    }
}