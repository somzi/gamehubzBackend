namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRegistrationRepository : IRepository<TournamentRegistrationEntity>
    {
        Task<List<TournamentRegistrationEntity>> GetByIds(List<Guid> ids);

        Task<List<TournamentRegistrationOverview>> GetPendingByTournamenId(Guid tournamentId);
    }
}