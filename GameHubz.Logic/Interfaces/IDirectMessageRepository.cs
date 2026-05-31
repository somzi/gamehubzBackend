namespace GameHubz.Logic.Interfaces
{
    public interface IDirectMessageRepository : IRepository<DirectMessageEntity>
    {
        Task<List<DirectMessageDto>> GetByChatId(Guid chatId, int take = 100, DateTime? before = null);

        Task MarkRead(Guid chatId, Guid readerUserId);
    }
}
