using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class HubRepository : BaseRepository<ApplicationContext, HubEntity>, IHubRepository
    {
        public HubRepository(ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<HubEntity>> GetByUserId(Guid userId)
        {
            return await this.BaseDbSet()
            .Where(x => x.UserId == userId)
            .ToListAsync();
        }

        public async Task<(Dictionary<Guid, string> ByHub, Dictionary<Guid, string> ByTournament)> GetAvatarUrls(
            IReadOnlyCollection<Guid> hubIds,
            IReadOnlyCollection<Guid> tournamentIds)
        {
            var byHub = new Dictionary<Guid, string>();
            var byTournament = new Dictionary<Guid, string>();

            if (hubIds.Count > 0)
            {
                var hubs = await this.BaseDbSet()
                    .Where(x => hubIds.Contains(x.Id!.Value) && x.AvatarUrl != null && x.AvatarUrl != "")
                    .Select(x => new { Id = x.Id!.Value, x.AvatarUrl })
                    .ToListAsync();

                foreach (var hub in hubs)
                {
                    byHub[hub.Id] = hub.AvatarUrl!;
                }
            }

            if (tournamentIds.Count > 0)
            {
                var tournaments = await this.BaseDbSet()
                    .Where(x => x.AvatarUrl != null && x.AvatarUrl != "")
                    .SelectMany(x => x.Tournaments!
                        .Where(t => tournamentIds.Contains(t.Id!.Value))
                        .Select(t => new { TournamentId = t.Id!.Value, x.AvatarUrl }))
                    .ToListAsync();

                foreach (var tournament in tournaments)
                {
                    byTournament[tournament.TournamentId] = tournament.AvatarUrl!;
                }
            }

            return (byHub, byTournament);
        }

        public Task<bool> UserOwnsAnyHub(Guid userId)
        {
            return this.BaseDbSet()
                .AnyAsync(x => x.UserId == userId);
        }

        public async Task<List<HubDto>> GetOverview()
        {
            return await this.BaseDbSet()
                .Select(x => new HubDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    UserId = x.UserId,
                    NumberOfUsers = x.UserHubs!.Count(),
                    NumberOfTournaments = x.Tournaments!.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted),
                    UserDisplayName = x.User.FirstName + " " + x.User.LastName,
                    IsPublic = x.IsPublic,
                    IsVerified = x.IsVerified
                })
                .ToListAsync();
        }

        public async Task<HubOverviewDto?> GetOverviewDtoById(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == hubId)
                .Select(x => new HubOverviewDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    NumberOfUsers = x.UserHubs!.Count(),
                    NumberOfTournaments = x.Tournaments!.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted),
                    UserId = x.UserId,
                    AvatarUrl = x.AvatarUrl,
                    OwnerName = x.User.Username,
                    IsPublic = x.IsPublic,
                    IsVerified = x.IsVerified,
                    CreatedOn = x.CreatedOn,
                    DiscordWebhookUrl = x.DiscordWebhookUrl,
                    DiscordNotificationSettings = x.DiscordNotificationSettings,
                    HubSocials = x.HubSocials!.Select(s => new HubSocialDto
                    {
                        Id = s.Id,
                        HubId = s.HubId,
                        Type = s.Type,
                        Username = s.Username
                    }).ToList()
                })
                .FirstOrDefaultAsync();
        }

        public Task<bool> IsUserFollowingHub(Guid userId, Guid id)
        {
            return this.BaseDbSet()
                .Where(x => x.Id == id)
                .AnyAsync(x => x.UserHubs != null && x.UserHubs.Any(uh => uh.UserId == userId));
        }

        public async Task<IEnumerable<HubDto>> GetHubsByUserId(Guid userId, int pageNumber, bool joined, string? search = null)
        {
            string? s = search?.ToLower();
            return await this.BaseDbSet()
                .Where(x => joined
                    ? x.UserHubs!.Any(uh => uh.UserId == userId) || x.UserId == userId
                    : !x.UserHubs!.Any(uh => uh.UserId == userId) && x.UserId != userId)
                .Where(x => s == null || x.Name.ToLower().StartsWith(s))
                // Postgres gives no ordering guarantee without ORDER BY, so Skip/Take alone can
                // repeat or drop hubs between pages. Name first (what the list shows), Id tiebreak.
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Id)
                .Skip(pageNumber * 10)
                .Take(10)
                .Select(x => new HubDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    UserId = x.UserId,
                    NumberOfUsers = x.UserHubs!.Count(),
                    NumberOfTournaments = x.Tournaments!.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted),
                    UserDisplayName = x.User.FirstName + " " + x.User.LastName,
                    AvatarUrl = x.AvatarUrl,
                    IsPublic = x.IsPublic,
                    IsVerified = x.IsVerified
                })
                .ToListAsync();
        }

        public async Task<List<Guid>> GetHubIdsByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(h => h.UserId == userId || (h.UserHubs != null && h.UserHubs.Any(uh => uh.UserId == userId)))
                .Select(h => h.Id!.Value)
                .ToListAsync();
        }

        public Task<HubEntity?> GetByDiscordGuildId(string guildId)
        {
            return this.BaseDbSet()
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.DiscordGuildId == guildId);
        }

        // Backfill support: hubs that already have a webhook URL configured but were saved before
        // guild-id auto-detection existed. Returns tracked entities so the caller can update &
        // persist in a single UnitOfWork pass.
        public Task<List<HubEntity>> GetWithWebhookMissingGuildId()
        {
            return this.BaseDbSet()
                .Where(h => h.DiscordWebhookUrl != null && h.DiscordGuildId == null)
                .ToListAsync();
        }

        // Aggregates every completed match played inside this hub, per user. Trophies count only
        // wins IN THIS HUB (hub-scoped), so the leaderboard stays comparable across sort modes.
        // Counted in SQL: each completed match becomes one row per side (the player on that side
        // and how the match went for them), grouped per player — so what comes back grows with the
        // number of players in the hub, not with its whole match history.
        public async Task<List<HubLeaderboardEntryDto>> GetHubLeaderboard(Guid hubId)
        {
            var completed = this.ContextBase.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.Status == MatchStatus.Completed
                    && m.Tournament!.HubId == hubId);

            // Team sub-matches carry the player directly (HomeUserId/AwayUserId); solo matches
            // resolve through the participant. No winner = draw.
            var sides = completed
                .Select(m => new
                {
                    UserId = m.HomeUserId ?? (m.HomeParticipant != null ? m.HomeParticipant.UserId : null),
                    IsDraw = m.WinnerParticipantId == null,
                    IsWin = m.WinnerParticipantId != null && m.WinnerParticipantId == m.HomeParticipantId,
                })
                .Concat(completed.Select(m => new
                {
                    UserId = m.AwayUserId ?? (m.AwayParticipant != null ? m.AwayParticipant.UserId : null),
                    IsDraw = m.WinnerParticipantId == null,
                    IsWin = m.WinnerParticipantId != null && m.WinnerParticipantId == m.AwayParticipantId,
                }));

            var totals = await sides
                .Where(s => s.UserId != null)
                .GroupBy(s => s.UserId!.Value)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalMatches = g.Count(),
                    Draws = g.Count(s => s.IsDraw),
                    Wins = g.Count(s => s.IsWin),
                })
                .ToListAsync();

            var userIds = totals.Select(t => t.UserId).ToList();
            var users = await this.ContextBase.Set<UserEntity>()
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id!.Value))
                .Select(u => new { Id = u.Id!.Value, u.Username, u.Nickname, u.AvatarUrl })
                .ToDictionaryAsync(u => u.Id);

            var trophies = await this.ContextBase.Set<TournamentEntity>()
                .AsNoTracking()
                .Where(t => t.HubId == hubId && t.WinnerUserId != null)
                .GroupBy(t => t.WinnerUserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var byUser = new Dictionary<Guid, HubLeaderboardEntryDto>();

            foreach (var t in totals)
            {
                users.TryGetValue(t.UserId, out var user);
                byUser[t.UserId] = new HubLeaderboardEntryDto
                {
                    UserId = t.UserId,
                    Username = user?.Username ?? "Unknown",
                    Nickname = user?.Nickname,
                    AvatarUrl = user?.AvatarUrl,
                    TotalMatches = t.TotalMatches,
                    Draws = t.Draws,
                    Wins = t.Wins,
                    // A draw has no winner, so draw and win never overlap — everything else lost.
                    Losses = t.TotalMatches - t.Draws - t.Wins,
                };
            }

            foreach (var kv in trophies)
            {
                if (byUser.TryGetValue(kv.Key, out var existing))
                    existing.Trophies = kv.Value;
                // A trophy without any completed matches (auto-forfeit champion?) still deserves
                // a row — leaderboard sort by trophies would otherwise miss them.
                else
                    byUser[kv.Key] = new HubLeaderboardEntryDto
                    {
                        UserId = kv.Key,
                        Username = "Unknown",
                        Trophies = kv.Value,
                    };
            }

            return byUser.Values.ToList();
        }
    }
}
