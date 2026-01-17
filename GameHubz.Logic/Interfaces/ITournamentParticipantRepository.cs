namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentParticipantRepository : IRepository<TournamentParticipantEntity>
    {
        Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId);
    }
}