namespace GameHubz.Logic.Interfaces
{
    public interface IMatchChatRepository : IRepository<MatchChatEntity>
    {
        Task<List<ChatMessageDto>> GetByMatchId(Guid matchId);
    }
}