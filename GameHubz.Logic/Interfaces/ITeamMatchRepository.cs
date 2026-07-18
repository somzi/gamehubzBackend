namespace GameHubz.Logic.Interfaces
{
    public interface ITeamMatchRepository : IRepository<TeamMatchEntity>
    {
        Task<TeamMatchEntity?> GetByIdWithSubMatches(Guid teamMatchId);

        Task<List<TeamMatchEntity>> GetByStageId(Guid stageId);

        /// <summary>Every team match of the tournament, across all stages (WB + LB), no includes —
        /// the settle pass scans scalar columns only.</summary>
        Task<List<TeamMatchEntity>> GetByTournamentId(Guid tournamentId);

        Task<TeamMatchDetailsProjection?> GetDetailsProjection(Guid teamMatchId);

        Task<TieBreakProjection?> GetTieBreakProjection(Guid teamMatchId);

        Task<bool> TryClaimForProcessing(Guid teamMatchId);
    }
}
