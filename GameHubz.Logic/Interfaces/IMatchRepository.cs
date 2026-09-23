namespace GameHubz.Logic.Interfaces
{
    public interface IMatchRepository : IRepository<MatchEntity>
    {
        Task<PlayerStatsDto> GetStatsByUserId(Guid userId);

        Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId, int pageSize, int pageNumber);

        Task<MatchEntity?> GetWithStage(Guid userId);

        /// <summary>
        /// Stamps CheckInResolvedOn on a still-scheduled, still-unruled match and reports whether
        /// this caller is the one that got it. The ready-check sweep's claim. Only succeeds while
        /// both check-in stamps still match the ones the ruling was decided on.
        /// </summary>
        Task<bool> TryClaimCheckInResolution(Guid matchId, DateTime resolvedOn, bool homeIn, bool awayIn);

        /// <summary>
        /// Atomically stores a result proposal and closes its ready check. Competes with the sweep's
        /// claim so exactly one of proposal or automatic ruling can win.
        /// </summary>
        Task<bool> TrySaveCheckInProposal(
            Guid matchId,
            int homeScore,
            int awayScore,
            string? gamesJson,
            Guid proposedByUserId,
            DateTime resolvedOn);

        /// <summary>Single-column, conditional write of one side's check-in stamp.</summary>
        Task<bool> TryStampCheckIn(Guid matchId, bool home, DateTime scheduledStart, DateTime checkedInOn);

        /// <summary>Closes the ready check on fixtures whose window had already opened when the check was switched on.</summary>
        Task<int> ExemptOpenCheckIns(Guid tournamentId, DateTime windowOpenedBefore, DateTime resolvedOn);

        /// <summary>Single-side write of offered hours; false when the match is already decided.</summary>
        Task<bool> TrySaveAvailabilitySlots(Guid matchId, bool home, string slotsJson, DateTime setOn);

        /// <summary>Pending → Scheduled at the given kick-off, dropping any old check-in state; false when no longer Pending.</summary>
        Task<bool> TryScheduleFromAvailability(Guid matchId, DateTime scheduledStart, DateTime modifiedOn);

        /// <summary>Confirms an externally agreed time without updating participants or other match data.</summary>
        Task<bool> TrySetScheduled(Guid matchId, DateTime scheduledStart, Guid modifiedByUserId);

        /// <summary>Clears only the schedule, availability and check-in columns of a Scheduled match.</summary>
        Task<bool> TryClearSchedule(Guid matchId, DateTime modifiedOn, Guid modifiedByUserId);

        Task<MatchEntity?> GetWithTournamentStage(Guid id);

        Task<bool> IsExistingByStageId(Guid? id);

        Task<bool> HasMatchesForStage(Guid value);

        Task<List<MatchOverviewDto>> GetByUser(Guid userId);

        Task<List<MatchBadgeRow>> GetActiveForUserBadge(Guid userId);

        Task<int> CountAdminHelpForHubs(List<Guid> hubIds);

        Task<List<TournamentCountRow>> GetAdminHelpCountsByTournament(List<Guid> hubIds);

        Task<int> CountPendingApprovalsForHubs(List<Guid> hubIds);

        Task<List<TournamentCountRow>> GetPendingApprovalCountsByTournament(List<Guid> hubIds);

        Task<MatchEntity?> GetWithParticipants(Guid matchId);

        Task<MatchAvailabilityDto?> GetAvailability(Guid id, Guid userId);

        Task<MatchAvailabilityAdminDto?> GetAvailabilityForAdmin(Guid id);

        Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId);

        Task<List<MatchPendingApprovalItemDto>> GetPendingApprovalMatches(Guid tournamentId);

        Task<bool> AreAllMatchesFinishedInTournament(Guid tournamentId);

        Task<List<MatchEntity>> GetByStageId(Guid groupStageId);

        /// <summary>
        /// The two participant ids as committed right now, or null when the match no longer exists.
        /// Result writes compare this with the row they loaded, so a report never lands on a fixture
        /// whose players changed underneath it.
        /// </summary>
        Task<(Guid? HomeParticipantId, Guid? AwayParticipantId)?> GetParticipantIds(Guid matchId);

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

        Task<List<MatchEntity>> GetByStageAndRoundWithParticipants(Guid stageId, int roundNumber);

        Task<MatchResultDetailDto?> GetWithEvidence(Guid id);

        Task<MatchUploadDto> GetForMatchEvidence(Guid matchId);

        Task<List<PerformanceDto>> GetPerformanceByUserId(Guid userId);

        Task<List<PerformanceV2Dto>> GetPerformanceByUserIdV2(Guid userId);

        Task<List<string>> GetOutcomesByUserId(Guid userId);

        Task<HeadToHeadDto> GetHeadToHead(Guid userId, Guid opponentId);
    }
}
