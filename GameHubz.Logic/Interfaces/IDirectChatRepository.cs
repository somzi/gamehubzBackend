namespace GameHubz.Logic.Interfaces
{
    public interface IDirectChatRepository : IRepository<DirectChatEntity>
    {
        Task<DirectChatEntity?> Find(Guid userAId, Guid userBId);

        Task<DirectChatEntity?> GetByIdForUser(Guid chatId, Guid userId);

        Task<DirectChatDto?> GetChatDtoForUser(Guid chatId, Guid userId);

        Task<List<DirectChatDto>> GetChatsForUser(Guid userId, string? search);
    }
}
