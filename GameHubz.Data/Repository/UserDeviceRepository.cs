using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserDeviceRepository : BaseRepository<ApplicationContext, UserDeviceEntity>, IUserDeviceRepository
    {
        public UserDeviceRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<UserDeviceEntity?> GetForUser(Guid userId, Guid deviceId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId);
        }

        public async Task<Dictionary<Guid, VerificationDeviceHistory>> GetDeviceHistoryForUsers(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var history = new Dictionary<Guid, VerificationDeviceHistory>();
            if (ids.Count == 0) return history;

            var rows = await this.BaseDbSet()
                .Where(d => ids.Contains(d.UserId))
                .Select(d => new { Id = d.Id!.Value, d.UserId, d.DeviceId, d.Platform, d.PlatformDeviceIdHash, d.CreatedOn })
                .ToListAsync();

            foreach (var account in rows.GroupBy(d => d.UserId))
            {
                // A missing platform id must never collapse unrelated phones into one. Matching is
                // account-local and only affects display: it does not merge installations or keys.
                var phones = account
                    .GroupBy(d => d.Platform == "android" && !string.IsNullOrWhiteSpace(d.PlatformDeviceIdHash)
                        ? ("android", d.PlatformDeviceIdHash)
                        : ("installation", d.DeviceId.ToString()))
                    .Select(g => new { FirstSeenOn = g.Min(d => d.CreatedOn), Installations = g.ToList() })
                    .ToList();
                var firstPhoneOn = phones.Min(p => p.FirstSeenOn);

                foreach (var phone in phones)
                {
                    // Later registrations must not retroactively turn the first phone into a new-phone warning.
                    var phoneHistory = new VerificationDeviceHistory(
                        phone.FirstSeenOn, firstPhoneOn < phone.FirstSeenOn, phones.Count);
                    foreach (var installation in phone.Installations)
                        history[installation.Id] = phoneHistory;
                }
            }

            return history;
        }

        public async Task<Dictionary<Guid, List<DeviceAccountSighting>>> GetAccountsOnSamePhone(IEnumerable<Guid> userDeviceIds)
        {
            var ids = userDeviceIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, List<DeviceAccountSighting>>();

            var rows = await this.BaseDbSet()
                .Where(d => ids.Contains(d.Id!.Value))
                .Select(d => new { Id = d.Id!.Value, d.DeviceId, d.PlatformDeviceIdHash })
                .ToListAsync();

            var deviceIds = rows.Select(r => r.DeviceId).Distinct().ToList();
            var hashes = rows.Where(r => r.PlatformDeviceIdHash != null).Select(r => r.PlatformDeviceIdHash!).Distinct().ToList();

            // One round trip for every phone on the screen; the matching is done in memory because a
            // match has two players and each has a handful of rows at most.
            var related = await this.BaseDbSet()
                .Where(d => deviceIds.Contains(d.DeviceId)
                    || (d.PlatformDeviceIdHash != null && hashes.Contains(d.PlatformDeviceIdHash)))
                .Select(d => new { d.UserId, d.DeviceId, d.PlatformDeviceIdHash, d.CreatedOn })
                .ToListAsync();

            // The row's own account is matched by the same rule as everyone else's, so "who was on
            // this phone first" compares like with like.
            return rows.ToDictionary(
                r => r.Id,
                r => related
                    .Where(x => x.DeviceId == r.DeviceId
                        || (r.PlatformDeviceIdHash != null && x.PlatformDeviceIdHash == r.PlatformDeviceIdHash))
                    .GroupBy(x => x.UserId)
                    .Select(g => new DeviceAccountSighting(g.Key, g.Min(x => x.CreatedOn)))
                    .OrderBy(s => s.FirstSeenOn)
                    .ToList());
        }
    }
}
