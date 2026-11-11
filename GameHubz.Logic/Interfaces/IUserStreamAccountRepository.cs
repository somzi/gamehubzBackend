using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IUserStreamAccountRepository : IRepository<UserStreamAccountEntity>
    {
        Task<List<UserStreamAccountEntity>> GetByUserId(Guid userId);

        Task<UserStreamAccountEntity?> GetByUserAndPlatform(Guid userId, SocialType platform);

        // The account a channel is linked to, whoever owns it — one channel maps to one GameHubz account.
        Task<UserStreamAccountEntity?> GetByExternalUserId(SocialType platform, string externalUserId);
    }
}
