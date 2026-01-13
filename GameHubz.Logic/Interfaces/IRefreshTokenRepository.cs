namespace GameHubz.Logic.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshTokenEntity>
    {
        RefreshTokenEntity? FindByUserIdAndTokenValue(Guid userId, string token);

        RefreshTokenEntity? FindByTokenValue(string token);
    }
}
