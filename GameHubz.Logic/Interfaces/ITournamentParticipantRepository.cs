namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentParticipantRepository : IRepository<TournamentParticipantEntity>
    {
        Task<List<TournamentParticipantEntity>> GetByGroupId(Guid? id);

        Task<List<TournamentParticipantEntity>> GetForLeagueResync(Guid tournamentId);

        Task<List<TournamentParticipantEntity>> GetEntitiesByTournamentId(Guid tournamentId);

        Task<List<TournamentParticipantEntity>> GetEntitiesByTournamentIdWithNames(Guid tournamentId);

        Task<List<TournamentParticipantEntity>> GetByGroupIdWithNames(Guid? id);

        Task<List<TournamentParticipantEntity>> GetByGroupIdsWithNames(List<Guid> groupIds);

        Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId);

        Task<List<TournamentOverview>> GetByUserId(Guid userid);

        Task<EntityListDto<TournamentOverview>> GetByUserIdPaged(Guid userid, int pageNumber, int pageSize);

        Task<int> CountTournamentsByUserId(Guid userId);

        Task<TournamentParticipantEntity> GetUserByTournamentId(Guid tournamentId, Guid userId);

        Task<List<TournamentParticipantEntity>> GetAllByTournamentAndUser(Guid tournamentId, Guid userId);

        Task<bool> ExistsForUser(Guid tournamentId, Guid userId);

        Task<bool> ExistsForTeam(Guid tournamentId, Guid teamId);

        Task<TournamentParticipantEntity?> GetByTeamId(Guid teamId);

        Task<List<Guid>> GetAllUserIdsByTournamentId(Guid tournamentId);

        /// <summary>How many entrants the tournament holds right now — read fresh, for the capacity check under LockEntry.</summary>
        Task<int> CountByTournament(Guid tournamentId);

        /// <summary>Participant id → the user currently holding that spot, for every solo entrant of the tournament.</summary>
        Task<Dictionary<Guid, Guid>> GetSoloUserIdsByParticipant(Guid tournamentId);
    }
}