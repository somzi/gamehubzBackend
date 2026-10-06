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
                    .Select(h => new NotificationSourceDto { Id = h.Id!.Value, Name = h.Name, AvatarUrl = h.AvatarUrl, IsLive = false });
            }
            else
            {
                // A registration still in play (pending or approved) — a rejected one is not a tournament of yours.
                var registrations = ContextBase.Set<TournamentRegistrationEntity>().Where(r => r.Status != TournamentRegistrationStatus.Rejected
                    && (r.UserId == userId || (r.Team != null && r.Team.Members.Any(m => m.UserId == userId))));
                sources = ContextBase.Set<TournamentEntity>()
                    // Nothing more will be sent about a finished, cancelled or deleted tournament, so
                    // there is nothing to mute.
                    .Where(t => t.Status != TournamentStatus.Completed
                        && t.Status != TournamentStatus.Cancelled
                        && t.Status != TournamentStatus.Deleted)
                    // Only the tournaments that are the user's: ones run from a hub they own or admin, ones
                    // they play in, and ones they've registered for. Tournaments that merely sit in a hub they
                    // follow are left out — muting the hub covers those.
                    .Where(t =>
                        t.Hub!.UserId == userId
                        || memberships.Any(m => m.HubId == t.HubId && (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin))
                        || t.TournamentParticipants!.Any(p => p.UserId == userId || (p.Team != null && p.Team.Members.Any(m => m.UserId == userId)))
                        || registrations.Any(r => r.TournamentId == t.Id))
                    // A tournament has no picture of its own; it wears its hub's avatar.
                    .Select(t => new NotificationSourceDto { Id = t.Id!.Value, Name = t.Name, AvatarUrl = t.Hub!.AvatarUrl, HubId = t.HubId, HubName = t.Hub!.Name, IsLive = t.Status == TournamentStatus.InProgress });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                sources = sources.Where(s => s.Name.ToLower().Contains(term));
            }
            // Live tournaments first (the ones sending notifications right now), then by name. Hubs are never live.
            var items = await sources.OrderByDescending(s => s.IsLive).ThenBy(s => s.Name).ThenBy(s => s.Id).Skip(page * pageSize).Take(pageSize + 1).ToListAsync();
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

        /// <summary>
        /// The same answer for every notification of a batch — a sweep's hundreds of reminders — from one
        /// read of the recipients' preferences (and nothing more when none of them muted anything), then
        /// one read per kind of ancestry the scopes need: matches, team matches, tournaments. The per-call
        /// method above paid all of that once for every notification.
        /// </summary>
        public async Task<List<List<MutedNotificationRecipient>>> GetMutedNotificationRecipientsBatch(IReadOnlyList<MutedRecipientsQuery> items)
        {
            var results = items.Select(_ => new List<MutedNotificationRecipient>()).ToList();
            var scoped = Enumerable.Range(0, items.Count).Where(i => !items[i].Scope.IsEmpty).ToList();
            if (scoped.Count == 0) return results;

            var userIds = scoped.SelectMany(i => items[i].UserIds).Distinct().ToList();
            var pushTokens = scoped.SelectMany(i => items[i].PushTokens).Distinct().ToList();
            var users = (await BaseDbSet().AsNoTracking()
                    .Where(u => (userIds.Contains(u.Id!.Value) || (u.PushToken != null && pushTokens.Contains(u.PushToken)))
                        && (u.MutedHubIdsJson != null || u.MutedTournamentIdsJson != null))
                    .Select(u => new { Id = u.Id!.Value, u.PushToken, u.DiscordUserId, u.MutedHubIdsJson, u.MutedTournamentIdsJson })
                    .ToListAsync())
                .Select(u => new
                {
                    u.Id,
                    u.PushToken,
                    u.DiscordUserId,
                    Hubs = NotificationSettingsDto.ReadIds(u.MutedHubIdsJson).ToHashSet(),
                    Tournaments = NotificationSettingsDto.ReadIds(u.MutedTournamentIdsJson).ToHashSet(),
                })
                .Where(u => u.Hubs.Count > 0 || u.Tournaments.Count > 0)
                .ToList();
            if (users.Count == 0) return results;

            // The same resolution as the single call: the tournament from the scope, else its match, else
            // its team match; the hub from the scope, else that tournament's.
            var tournamentOf = scoped.ToDictionary(i => i, i => items[i].Scope.TournamentId);

            var matchIds = scoped.Where(i => tournamentOf[i] == null && items[i].Scope.MatchId != null)
                .Select(i => items[i].Scope.MatchId!.Value).Distinct().ToList();
            if (matchIds.Count > 0)
            {
                var byMatch = await ContextBase.Set<MatchEntity>().IgnoreQueryFilters()
                    .Where(m => matchIds.Contains(m.Id!.Value))
                    .Select(m => new { Id = m.Id!.Value, m.TournamentId })
                    .ToDictionaryAsync(m => m.Id, m => m.TournamentId);
                foreach (int i in scoped.Where(i => tournamentOf[i] == null && items[i].Scope.MatchId != null))
                    if (byMatch.TryGetValue(items[i].Scope.MatchId!.Value, out var tournamentId)) tournamentOf[i] = tournamentId;
            }

            var teamMatchIds = scoped.Where(i => tournamentOf[i] == null && items[i].Scope.TeamMatchId != null)
                .Select(i => items[i].Scope.TeamMatchId!.Value).Distinct().ToList();
            if (teamMatchIds.Count > 0)
            {
                var byTeamMatch = await ContextBase.Set<TeamMatchEntity>().IgnoreQueryFilters()
                    .Where(m => teamMatchIds.Contains(m.Id!.Value))
                    .Select(m => new { Id = m.Id!.Value, m.TournamentId })
                    .ToDictionaryAsync(m => m.Id, m => m.TournamentId);
                foreach (int i in scoped.Where(i => tournamentOf[i] == null && items[i].Scope.TeamMatchId != null))
                    if (byTeamMatch.TryGetValue(items[i].Scope.TeamMatchId!.Value, out var tournamentId)) tournamentOf[i] = tournamentId;
            }

            var hubOf = scoped.ToDictionary(i => i, i => items[i].Scope.HubId);
            var tournamentIds = scoped.Where(i => hubOf[i] == null && tournamentOf[i] != null)
                .Select(i => tournamentOf[i]!.Value).Distinct().ToList();
            if (tournamentIds.Count > 0)
            {
                var byTournament = await ContextBase.Set<TournamentEntity>().IgnoreQueryFilters()
                    .Where(t => tournamentIds.Contains(t.Id!.Value))
                    .Select(t => new { Id = t.Id!.Value, t.HubId })
                    .ToDictionaryAsync(t => t.Id, t => t.HubId);
                foreach (int i in scoped.Where(i => hubOf[i] == null && tournamentOf[i] != null))
                    if (byTournament.TryGetValue(tournamentOf[i]!.Value, out var hubId)) hubOf[i] = hubId;
            }

            foreach (int i in scoped)
            {
                var item = items[i];
                Guid? hub = hubOf[i];
                Guid? tournament = tournamentOf[i];
                results[i] = users
                    .Where(u => (item.UserIds.Contains(u.Id) || (u.PushToken != null && item.PushTokens.Contains(u.PushToken)))
                        && ((hub.HasValue && u.Hubs.Contains(hub.Value)) || (tournament.HasValue && u.Tournaments.Contains(tournament.Value))))
                    .Select(u => new MutedNotificationRecipient { UserId = u.Id, PushToken = u.PushToken, DiscordUserId = u.DiscordUserId })
                    .ToList();
            }

            return results;
        }
    }
}
