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
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count() : 0,
                    NumberOfTournaments = x.Tournaments != null
                        ? x.Tournaments.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted)
                        : 0,
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
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count : 0,
                    NumberOfTournaments = x.Tournaments != null
                        ? x.Tournaments.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted)
                        : 0,
                    UserId = x.UserId,
                    AvatarUrl = x.AvatarUrl,
                    OwnerName = x.User.Username,
                    IsPublic = x.IsPublic,
                    IsVerified = x.IsVerified,
                    CreatedOn = x.CreatedOn,
                    DiscordWebhookUrl = x.DiscordWebhookUrl,
                    DiscordNotificationSettings = x.DiscordNotificationSettings,
                    HubSocials = x.HubSocials != null
                            ? x.HubSocials.Select(s => new HubSocialDto
                            {
                                Id = s.Id,
                                HubId = s.HubId,
                                Type = s.Type,
                                Username = s.Username
                            }).ToList()
                            : new List<HubSocialDto>()
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
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count() : 0,
                    NumberOfTournaments = x.Tournaments != null
                        ? x.Tournaments.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted)
                        : 0,
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
        // Done in memory because the dual participant/team-match model plus subquery for trophies
        // makes the equivalent SQL fragile — and the row count is bounded by "distinct players in
        // one hub", not the whole platform.
        public async Task<List<HubLeaderboardEntryDto>> GetHubLeaderboard(Guid hubId)
        {
            var matches = await this.ContextBase.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.Status == MatchStatus.Completed
                    && m.Tournament!.HubId == hubId)
                .Include(m => m.HomeParticipant).ThenInclude(p => p!.User)
                .Include(m => m.AwayParticipant).ThenInclude(p => p!.User)
                .Include(m => m.HomeUser)
                .Include(m => m.AwayUser)
                .Select(m => new
                {
                    m.WinnerParticipantId,
                    m.HomeParticipantId,
                    m.AwayParticipantId,
                    m.HomeUserId,
                    m.AwayUserId,
                    HomeParticipantUserId = m.HomeParticipant != null ? m.HomeParticipant.UserId : (Guid?)null,
                    AwayParticipantUserId = m.AwayParticipant != null ? m.AwayParticipant.UserId : (Guid?)null,
                    HomeUsername = m.HomeUser != null ? m.HomeUser.Username
                        : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.Username : null),
                    HomeNickname = m.HomeUser != null ? m.HomeUser.Nickname
                        : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.Nickname : null),
                    HomeAvatar = m.HomeUser != null ? m.HomeUser.AvatarUrl
                        : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.AvatarUrl : null),
                    AwayUsername = m.AwayUser != null ? m.AwayUser.Username
                        : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.Username : null),
                    AwayNickname = m.AwayUser != null ? m.AwayUser.Nickname
                        : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.Nickname : null),
                    AwayAvatar = m.AwayUser != null ? m.AwayUser.AvatarUrl
                        : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.AvatarUrl : null),
                })
                .ToListAsync();

            var trophies = await this.ContextBase.Set<TournamentEntity>()
                .AsNoTracking()
                .Where(t => t.HubId == hubId && t.WinnerUserId != null)
                .GroupBy(t => t.WinnerUserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var byUser = new Dictionary<Guid, HubLeaderboardEntryDto>();

            foreach (var m in matches)
            {
                Guid? homeUserId = m.HomeUserId ?? m.HomeParticipantUserId;
                Guid? awayUserId = m.AwayUserId ?? m.AwayParticipantUserId;

                Accumulate(homeUserId, m.HomeUsername, m.HomeNickname, m.HomeAvatar,
                    isDraw: m.WinnerParticipantId == null,
                    isWin: m.WinnerParticipantId != null && m.WinnerParticipantId == m.HomeParticipantId);
                Accumulate(awayUserId, m.AwayUsername, m.AwayNickname, m.AwayAvatar,
                    isDraw: m.WinnerParticipantId == null,
                    isWin: m.WinnerParticipantId != null && m.WinnerParticipantId == m.AwayParticipantId);
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

            void Accumulate(Guid? userId, string? username, string? nickname, string? avatar, bool isDraw, bool isWin)
            {
                if (userId == null) return;

                if (!byUser.TryGetValue(userId.Value, out var entry))
                {
                    entry = new HubLeaderboardEntryDto
                    {
                        UserId = userId.Value,
                        Username = username ?? "Unknown",
                        Nickname = nickname,
                        AvatarUrl = avatar,
                    };
                    byUser[userId.Value] = entry;
                }

                entry.TotalMatches++;
                if (isDraw) entry.Draws++;
                else if (isWin) entry.Wins++;
                else entry.Losses++;
            }
        }
    }
}