namespace GameHubz.Logic.Interfaces
{
    public interface IUserDeviceRepository : IRepository<UserDeviceEntity>
    {
        /// <summary>This account's row for one phone, tracked so a re-registration can update it in place.</summary>
        Task<UserDeviceEntity?> GetForUser(Guid userId, Guid deviceId);

        /// <summary>History keyed by device row, recognizing Android reinstalls within each account.</summary>
        Task<Dictionary<Guid, VerificationDeviceHistory>> GetDeviceHistoryForUsers(IEnumerable<Guid> userIds);

        /// <summary>
        /// For each device row, every account registered on the same phone — the same installation id,
        /// or the same platform id hash (which survives an Android reinstall) — the row's own account
        /// included, each with when it first appeared there, earliest first.
        /// </summary>
        Task<Dictionary<Guid, List<DeviceAccountSighting>>> GetAccountsOnSamePhone(IEnumerable<Guid> userDeviceIds);
    }
}
