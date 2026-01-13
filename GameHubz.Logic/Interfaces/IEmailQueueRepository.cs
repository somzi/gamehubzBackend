namespace GameHubz.Logic.Interfaces
{
    public interface IEmailQueueRepository : IRepository<EmailQueueEntity>
    {
        Task<EmailQueueEntity?> GetNextEmailQueue();
    }
}
