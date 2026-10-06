namespace GameHubz.Logic.Interfaces
{
    public interface IHubRepository : IRepository<HubEntity>
    {
        Task<List<HubDto>> GetOverview();

        Task<List<HubEntity>> GetByUserId(Guid userId);

        Task<bool> UserOwnsAnyHub(Guid userId);

        Task<HubOverviewDto?> GetOverviewDtoById(Guid hubId);

        Task<bool> IsUserFollowingHub(Guid userId, Guid id);

        Task<IEnumerable<HubDto>> GetHubsByUserId(Guid userId, int pageNumber, bool joined, string? search = null);

        Task<List<Guid>> GetHubIdsByUserId(Guid userId);

        Task<HubEntity?> GetByDiscordGuildId(string guildId);

        Task<List<HubEntity>> GetWithWebhookMissingGuildId();

        Task<List<HubLeaderboardEntryDto>> GetHubLeaderboard(Guid hubId);

        /// <summary>
        /// Hub avatars for a page of notifications: by hub id, and by tournament id (the tournament's
        /// hub). Only hubs that have an avatar appear.
        /// </summary>
        Task<(Dictionary<Guid, string> ByHub, Dictionary<Guid, string> ByTournament)> GetAvatarUrls(
            IReadOnlyCollection<Guid> hubIds,
            IReadOnlyCollection<Guid> tournamentIds);
    }
}