namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentPlayerDeviceRepository : IRepository<TournamentPlayerDeviceEntity>
    {
        Task LockPlayer(Guid tournamentId, Guid userId);
        Task<List<TournamentPlayerDeviceEntity>> GetForPlayer(Guid tournamentId, Guid userId);
        Task<List<TournamentPlayerDeviceEntity>> GetPending(Guid tournamentId);
        Task<bool> IsRegistered(Guid tournamentId, Guid userId);
        Task<List<TournamentCountRow>> GetPendingCountsByTournament(List<Guid> hubIds);
    }
}
