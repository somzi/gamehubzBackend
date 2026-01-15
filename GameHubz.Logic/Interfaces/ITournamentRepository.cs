namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);
    }
}