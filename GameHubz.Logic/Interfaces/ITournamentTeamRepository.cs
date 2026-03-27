namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentTeamRepository : IRepository<TournamentTeamEntity>
    {
        Task<List<TournamentTeamEntity>> GetByTournamentId(Guid tournamentId);

        Task<List<TeamDto>> GetTeamsDtoByTournamentId(Guid tournamentId);
        Task<List<TournamentTeamEntity>> GetFinalByTournamentId(Guid tournamentId);

        Task<TournamentTeamEntity?> GetByIdWithMembers(Guid teamId);

        Task<TournamentTeamEntity> GetSingleByTournamentId(Guid tournamentId, Guid userId);

        Task<TeamDto> GetTeamDtoByTournamentId(Guid tournamentId, Guid userId);

        Task<TeamJoinData?> GetTeamForJoin(Guid teamId, Guid userId);
    }
}
