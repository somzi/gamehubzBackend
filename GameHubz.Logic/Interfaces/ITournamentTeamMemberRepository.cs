namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentTeamMemberRepository : IRepository<TournamentTeamMemberEntity>
    {
        Task<List<TournamentTeamMemberEntity>> GetByTeamId(Guid teamId);

        Task<List<TournamentTeamMemberEntity>> GetByUserId(Guid userId);

        Task<bool> ExistsInTournament(Guid userId, Guid tournamentId);
    }
}
