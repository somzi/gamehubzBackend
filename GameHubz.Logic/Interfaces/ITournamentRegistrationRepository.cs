namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRegistrationRepository : IRepository<TournamentRegistrationEntity>
    {
        Task<List<TournamentRegistrationEntity>> GetByIds(List<Guid> ids);

        Task<List<TournamentRegistrationOverview>> GetPendingByTournamenId(Guid tournamentId);

        Task<TournamentRegistrationEntity> GetUserByTournamentId(Guid tournamentId, Guid userId);

        Task<TournamentRegistrationEntity> GetWithTournament(Guid registrationId);

        Task<TournamentRegistrationEntity?> GetByTeamId(Guid teamId);
    }
}