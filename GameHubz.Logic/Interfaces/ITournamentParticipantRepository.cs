namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentParticipantRepository : IRepository<TournamentParticipantEntity>
    {
        Task<List<TournamentParticipantEntity>> GetByGroupId(Guid? id);

        Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId);

        Task<List<TournamentOverview>> GetByUserId(Guid userid);
    }
}