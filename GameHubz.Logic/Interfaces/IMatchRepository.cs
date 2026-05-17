namespace GameHubz.Logic.Interfaces
{
    public interface IMatchRepository : IRepository<MatchEntity>
    {
        Task<PlayerStatsDto> GetStatsByUserId(Guid userId);

        Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId, int pageSize, int pageNumber);

        Task<MatchEntity?> GetWithStage(Guid userId);

        Task<MatchEntity?> GetWithTournamentStage(Guid id);

        Task<bool> IsExistingByStageId(Guid? id);

        Task<bool> HasMatchesForStage(Guid value);

        Task<List<MatchOverviewDto>> GetByUser(Guid userId);

        Task<MatchEntity?> GetWithParticipants(Guid matchId);

        Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId);

        Task<bool> AreAllMatchesFinishedInTournament(Guid tournamentId);

        Task<List<MatchEntity>> GetByStageId(Guid groupStageId);

        Task<List<MatchEntity>> GetByTournamentAndRound(Guid tournamentId, int roundNumber);

        Task<MatchResultDetailDto> GetWithEvidence(Guid id);

        Task<MatchUploadDto> GetForMatchEvidence(Guid matchId);

        Task<List<PerformanceDto>> GetPerformanceByUserId(Guid userId);

        Task<List<PerformanceV2Dto>> GetPerformanceByUserIdV2(Guid userId);
    }
}