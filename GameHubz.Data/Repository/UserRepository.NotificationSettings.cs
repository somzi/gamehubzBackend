using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public partial class UserRepository
    {
        public async Task<NotificationSourcePageDto> GetNotificationSources(Guid userId, string kind, int page, string? search)
        {
            const int pageSize = 20;
            var memberships = ContextBase.Set<UserHubEntity>().Where(m => m.UserId == userId);
            IQueryable<NotificationSourceDto> sources;
            if (kind == "hubs")
            {
                sources = ContextBase.Set<HubEntity>()
                    .Where(h => h.UserId == userId || memberships.Any(m => m.HubId == h.Id))
                    .Select(h => new NotificationSourceDto { Id = h.Id!.Value, Name = h.Name, AvatarUrl = h.AvatarUrl });
            }
            else
            {
                var user = await BaseDbSet().Where(u => u.Id == userId).Select(u => new { u.Region, u.Country }).SingleAsync();
                var registrations = ContextBase.Set<TournamentRegistrationEntity>().Where(r => r.UserId == userId
                    || (r.Team != null && r.Team.Members.Any(m => m.UserId == userId)));
                sources = ContextBase.Set<TournamentEntity>()
                    .Where(t =>
                        t.Hub!.UserId == userId
                        || memberships.Any(m => m.HubId == t.HubId && (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin))
                        || t.TournamentParticipants!.Any(p => p.UserId == userId || (p.Team != null && p.Team.Members.Any(m => m.UserId == userId)))
                        || registrations.Any(r => r.TournamentId == t.Id)
                        || (memberships.Any(m => m.HubId == t.HubId)
                            && (t.Status != TournamentStatus.Draft || t.RegistrationOpensAt != null)
                            && t.Status != TournamentStatus.Deleted
                            && (!t.IsExclusive || memberships.Any(m => m.HubId == t.HubId && m.HubRole == HubRole.HubExclusive))
                            && ((t.Countries == null && (t.Region == user.Region || t.Region == RegionType.GLOBAL))
                                || (user.Country != null && t.Countries != null && t.Countries.Contains(user.Country)))))
                    // A tournament has no picture of its own; it wears its hub's avatar.
                    .Select(t => new NotificationSourceDto { Id = t.Id!.Value, Name = t.Name, AvatarUrl = t.Hub!.AvatarUrl, HubId = t.HubId, HubName = t.Hub!.Name });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                sources = sources.Where(s => s.Name.ToLower().Contains(term));
            }
            var items = await sources.OrderBy(s => s.Name).ThenBy(s => s.Id).Skip(page * pageSize).Take(pageSize + 1).ToListAsync();
            bool hasMore = items.Count > pageSize;
            if (hasMore) items.RemoveAt(pageSize);
            return new NotificationSourcePageDto { Items = items, NextPage = hasMore ? page + 1 : null };
        }

        public async Task<List<MutedNotificationRecipient>> GetMutedNotificationRecipients(NotificationScope scope, List<Guid> userIds, List<string> pushTokens, List<string> discordUserIds)
        {
            if (scope.IsEmpty) return new();
            // One preference read per recipient batch; skip resolving match ancestry when nobody has exclusions.
            var users = await BaseDbSet().AsNoTracking()
                .Where(u => (userIds.Contains(u.Id!.Value) || (u.PushToken != null && pushTokens.Contains(u.PushToken))
                    || (u.DiscordUserId != null && discordUserIds.Contains(u.DiscordUserId)))
                    && (u.MutedHubIdsJson != null || u.MutedTournamentIdsJson != null))
                .Select(u => new { u.Id, u.PushToken, u.DiscordUserId, u.MutedHubIdsJson, u.MutedTournamentIdsJson }).ToListAsync();
            if (users.Count == 0) return new();

            Guid? tournamentId = scope.TournamentId;
            if (tournamentId == null && scope.MatchId != null)
                tournamentId = await ContextBase.Set<MatchEntity>().IgnoreQueryFilters().Where(m => m.Id == scope.MatchId).Select(m => (Guid?)m.TournamentId).FirstOrDefaultAsync();
            if (tournamentId == null && scope.TeamMatchId != null)
                tournamentId = await ContextBase.Set<TeamMatchEntity>().IgnoreQueryFilters().Where(m => m.Id == scope.TeamMatchId).Select(m => (Guid?)m.TournamentId).FirstOrDefaultAsync();
            Guid? hubId = scope.HubId;
            if (hubId == null && tournamentId != null)
                hubId = await ContextBase.Set<TournamentEntity>().IgnoreQueryFilters().Where(t => t.Id == tournamentId).Select(t => t.HubId).FirstOrDefaultAsync();

            return users.Where(u => (hubId.HasValue && NotificationSettingsDto.ReadIds(u.MutedHubIdsJson).Contains(hubId.Value))
                    || (tournamentId.HasValue && NotificationSettingsDto.ReadIds(u.MutedTournamentIdsJson).Contains(tournamentId.Value)))
                .Select(u => new MutedNotificationRecipient { UserId = u.Id!.Value, PushToken = u.PushToken, DiscordUserId = u.DiscordUserId }).ToList();
        }
    }
}
