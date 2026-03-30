namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentParticipantRepository : IRepository<TournamentParticipantEntity>
    {
        Task<List<TournamentParticipantEntity>> GetByGroupId(Guid? id);

        Task<List<TournamentParticipantEntity>> GetByGroupIdWithNames(Guid? id);

        Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId);

        Task<List<TournamentOverview>> GetByUserId(Guid userid);

        Task<EntityListDto<TournamentOverview>> GetByUserIdPaged(Guid userid, int pageNumber, int pageSize);

        Task<TournamentParticipantEntity> GetUserByTournamentId(Guid tournamentId, Guid userId);

        Task<TournamentParticipantEntity?> GetByTeamId(Guid teamId);
    }
}