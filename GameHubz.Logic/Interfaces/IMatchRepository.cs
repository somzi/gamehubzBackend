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

        Task<List<MatchBadgeRow>> GetActiveForUserBadge(Guid userId);

        Task<int> CountAdminHelpForHubs(List<Guid> hubIds);

        Task<List<TournamentCountRow>> GetAdminHelpCountsByTournament(List<Guid> hubIds);

        Task<MatchEntity?> GetWithParticipants(Guid matchId);

        Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId);

        Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId);

        Task<List<MatchPendingApprovalItemDto>> GetPendingApprovalMatches(Guid tournamentId);

        Task<bool> AreAllMatchesFinishedInTournament(Guid tournamentId);

        Task<List<MatchEntity>> GetByStageId(Guid groupStageId);

        // Every match in a tournament. Spans all stages — needed for double elimination, where a
        // Losers-Bracket match's loser-edge feeder lives in the Winners stage. Used by the
        // double-walkover settle pass, which reloads committed state between saves.
        Task<List<MatchEntity>> GetAllByTournamentId(Guid tournamentId);

        // Clears the EF change tracker so the next read starts from committed state. The settle pass
        // saves-then-reloads in a loop, so it must drop prior instances to avoid identity collisions.
        void DetachAll();

        Task<List<GroupMatchStatsRow>> GetCompletedSoloMatchStatsForGroup(Guid stageId, Guid? groupId, Guid? excludeMatchId);

        Task<List<MatchEntity>> GetByTournamentAndRound(Guid tournamentId, int roundNumber);

        Task<List<MatchEntity>> GetByStageAndRound(Guid stageId, int roundNumber);

        Task<MatchResultDetailDto> GetWithEvidence(Guid id);

        Task<MatchUploadDto> GetForMatchEvidence(Guid matchId);

        Task<List<PerformanceDto>> GetPerformanceByUserId(Guid userId);

        Task<List<PerformanceV2Dto>> GetPerformanceByUserIdV2(Guid userId);
    }
}