namespace GameHubz.Logic.Interfaces
{
    public interface IHubVerificationRequestRepository : IRepository<HubVerificationRequestEntity>
    {
        Task<HubVerificationRequestEntity?> GetLatestForHub(Guid hubId);

        Task<HubVerificationRequestEntity?> GetPendingForHub(Guid hubId);
    }
}
