namespace GameHubz.Logic.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshTokenEntity>
    {
        RefreshTokenEntity? FindByUserIdAndTokenValue(Guid userId, string token);

        RefreshTokenEntity? FindByTokenValue(string token);

        // Revokes every refresh token a user holds. Used on password change/reset so a credential
        // change invalidates all existing sessions (a previously-leaked token can no longer be exchanged).
        Task HardDeleteAllByUserId(Guid userId);
    }
}
