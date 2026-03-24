namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentTeamRepository : IRepository<TournamentTeamEntity>
    {
        Task<List<TournamentTeamEntity>> GetByTournamentId(Guid tournamentId);
        Task<List<TournamentTeamEntity>> GetFinalByTournamentId(Guid tournamentId);

        Task<TournamentTeamEntity?> GetByIdWithMembers(Guid teamId);

        Task<TournamentTeamEntity> GetSingleByTournamentId(Guid tournamentId, Guid userId);
    }
}
