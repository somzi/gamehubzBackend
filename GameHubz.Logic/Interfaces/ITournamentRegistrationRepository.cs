namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRegistrationRepository : IRepository<TournamentRegistrationEntity>
    {
        Task<List<TournamentRegistrationEntity>> GetByIds(List<Guid> ids);

        Task<List<TournamentRegistrationOverview>> GetPendingByTournamenId(Guid tournamentId);

        Task<TournamentRegistrationEntity> GetUserByTournamentId(Guid tournamentId, Guid userId);

        Task<List<TournamentRegistrationEntity>> GetAllByTournamentAndUser(Guid tournamentId, Guid userId);

        Task<bool> ExistsNonRejected(Guid tournamentId, Guid? userId, Guid? teamId);

        Task<TournamentRegistrationEntity> GetWithTournament(Guid registrationId);

        Task<TournamentRegistrationEntity?> GetByTeamId(Guid teamId);

        Task<int> CountPendingForHubs(List<Guid> hubIds);

        Task<List<TournamentCountRow>> GetPendingCountsByTournament(List<Guid> hubIds);
    }
}