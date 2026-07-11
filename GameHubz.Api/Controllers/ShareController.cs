using GameHubz.Api.Share;
using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Public share pages for share.codespheresolutions.dev.
    /// Each route serves server-rendered HTML: link-preview crawlers read the
    /// Open Graph tags, real visitors get a deep-link attempt into the app with
    /// a store-link fallback. Hits on existing entities are recorded in ShareLog
    /// (per-IP daily cap; not-found hits are never logged).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class ShareController : ControllerBase
    {
        private readonly ApplicationContext context;
        private readonly ShareLinksConfig config;
        private readonly ILogger<ShareController> logger;
        private readonly ICacheService cacheService;

        public ShareController(
            ApplicationContext context,
            IOptions<ShareLinksConfig> options,
            ILogger<ShareController> logger,
            ICacheService cacheService)
        {
            this.context = context;
            this.config = options.Value;
            this.logger = logger;
            this.cacheService = cacheService;
        }

        // Cached projection of the public player share card (F83). Flat primitives only, so it
        // round-trips through the cache serializer without depending on PlayerScoreboard's shape.
        private sealed record UserShareCache(
            string Title, string Description, string? ImageUrl,
            int Total, int WinRate, int Wins, int Draws, int Losses, int Trophies);

        [HttpGet("/tournament/{id:guid}")]
        public async Task<ContentResult> Tournament(Guid id)
        {
            var data = await context.Set<TournamentEntity>()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Name,
                    x.Format,
                    x.StartDate,
                    x.MaxPlayers,
                    x.Prize,
                    x.PrizeCurrency,
                    x.IsTeamTournament,
                    x.TeamSize,
                    HubName = x.Hub != null ? x.Hub.Name : null,
                    HubAvatarUrl = x.Hub != null ? x.Hub.AvatarUrl : null,
                    ApprovedCount = x.TournamentRegistrations!
                        .Count(r => r.Status == TournamentRegistrationStatus.Approved),
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFoundPage("Tournament", $"tournament/{id}");
            }

            var stats = new List<ShareStat> { new("Format", Humanize(data.Format.ToString())) };

            if (data.IsTeamTournament)
            {
                stats.Add(new("Teams", data.TeamSize > 0 ? $"{data.TeamSize}v{data.TeamSize}" : "Team tournament"));
            }
            else
            {
                stats.Add(new("Players", data.MaxPlayers > 0
                    ? $"{data.ApprovedCount}/{data.MaxPlayers}"
                    : $"{data.ApprovedCount}"));
            }

            if (data.Prize > 0)
            {
                stats.Add(new("Prize", $"{data.Prize} {CurrencyLabel(data.PrizeCurrency)}"));
            }

            if (data.StartDate.HasValue)
            {
                stats.Add(new("Starts", $"{data.StartDate.Value:MMM d, yyyy}"));
            }

            var parts = stats.Select(s => $"{s.Label}: {s.Value}").ToList();
            if (!string.IsNullOrWhiteSpace(data.HubName))
            {
                parts.Insert(0, $"by {data.HubName}");
            }

            return await SharePage(
                ShareEntityType.Tournament,
                "Tournament",
                webPath: $"tournament/{id}",
                deepPath: $"tournament/{id}",
                title: data.Name,
                description: string.Join(" · ", parts),
                imageUrl: data.HubAvatarUrl,
                entityId: id,
                stats: stats,
                contextText: data.HubName,
                contextImageUrl: data.HubAvatarUrl);
        }

        [HttpGet("/hub/{id:guid}")]
        public async Task<ContentResult> Hub(Guid id)
        {
            var data = await context.Set<HubEntity>()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Name,
                    x.Description,
                    x.AvatarUrl,
                    MemberCount = x.UserHubs!.Count(),
                    TournamentCount = x.Tournaments!.Count(t => t.Status != TournamentStatus.Cancelled && t.Status != TournamentStatus.Deleted),
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFoundPage("Hub", $"hub/{id}");
            }

            string stats = $"{data.MemberCount} members · {data.TournamentCount} tournaments";
            string description = string.IsNullOrWhiteSpace(data.Description)
                ? stats
                : $"{stats} · {Truncate(data.Description, 120)}";

            return await SharePage(
                ShareEntityType.Hub,
                "Hub",
                webPath: $"hub/{id}",
                deepPath: $"hub/{id}",
                title: data.Name,
                description: description!,
                imageUrl: data.AvatarUrl,
                entityId: id);
        }

        [HttpGet("/user/{id:guid}")]
        public async Task<ContentResult> UserProfile(Guid id)
        {
            // F83: this page is [AllowAnonymous] and the stats below are a full scan over MatchEntity plus
            // a tournament subquery. Cache the computed card per user (5-min TTL) so repeated hits, link
            // crawlers, or a GUID-looping attacker don't re-run the heavy aggregation on every request.
            string cacheKey = $"share:user:{id}";
            var cachedCard = await this.cacheService.GetAsync<UserShareCache>(cacheKey);
            if (cachedCard != null)
            {
                return await SharePage(
                    ShareEntityType.User,
                    "Player",
                    webPath: $"user/{id}",
                    deepPath: $"player/{id}",
                    title: cachedCard.Title,
                    description: cachedCard.Description,
                    imageUrl: cachedCard.ImageUrl,
                    entityId: id,
                    scoreboard: new PlayerScoreboard(cachedCard.Total, cachedCard.WinRate, cachedCard.Wins, cachedCard.Draws, cachedCard.Losses, cachedCard.Trophies));
            }

            var data = await context.Set<UserEntity>()
                .AsNoTracking()
                .Where(x => x.Id == id && x.IsActive)
                .Select(x => new
                {
                    x.Username,
                    x.Nickname,
                    x.AvatarUrl,
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFoundPage("Player", $"user/{id}");
            }

            string displayName = string.IsNullOrWhiteSpace(data.Nickname) ? data.Username : data.Nickname!;

            // Same predicates as MatchRepository.GetStatsByUserId and
            // TournamentRepository.GetNumberOfTournamentsWonByUserId, so the share
            // card shows the exact numbers the in-app profile shows.
            var matchStats = await context.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m =>
                    ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null && (m.HomeParticipant!.UserId == id || m.AwayParticipant!.UserId == id))
                    || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null && (m.HomeUserId == id || m.AwayUserId == id)))
                    && m.Status == MatchStatus.Completed)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Wins = g.Count(m => m.WinnerParticipantId != null &&
                        ((m.HomeUserId == id || (m.TeamMatchId == null && m.HomeParticipant!.UserId == id))
                            ? m.WinnerParticipantId == m.HomeParticipantId
                            : m.WinnerParticipantId == m.AwayParticipantId)),
                    Losses = g.Count(m => m.WinnerParticipantId != null &&
                        ((m.HomeUserId == id || (m.TeamMatchId == null && m.HomeParticipant!.UserId == id))
                            ? m.WinnerParticipantId != m.HomeParticipantId
                            : m.WinnerParticipantId != m.AwayParticipantId)),
                })
                .FirstOrDefaultAsync();

            int trophies = await context.Set<TournamentEntity>()
                .AsNoTracking()
                .CountAsync(t => t.WinnerUserId == id
                    || (t.WinnerTeamId != null && t.WinnerTeam!.Members.Any(m => m.UserId == id)));

            int total = matchStats?.Total ?? 0;
            int wins = matchStats?.Wins ?? 0;
            int losses = matchStats?.Losses ?? 0;
            int draws = total - wins - losses;
            int winRate = total > 0 ? (int)Math.Round(wins * 100.0 / total) : 0;

            var scoreboard = new PlayerScoreboard(total, winRate, wins, draws, losses, trophies);

            string description = total > 0
                ? $"{total} matches · {wins}W {losses}L {draws}D · {winRate}% win rate · {trophies} {(trophies == 1 ? "trophy" : "trophies")} on {config.AppName}"
                : $"Player profile on {config.AppName} — tournaments, match history and stats.";

            await this.cacheService.SetAsync(
                cacheKey,
                new UserShareCache(displayName, description, data.AvatarUrl, total, winRate, wins, draws, losses, trophies),
                TimeSpan.FromMinutes(5));

            return await SharePage(
                ShareEntityType.User,
                "Player",
                webPath: $"user/{id}",
                // The in-app route for a user profile is player/:id, not user/:id.
                deepPath: $"player/{id}",
                title: displayName,
                description: description,
                imageUrl: data.AvatarUrl,
                entityId: id,
                scoreboard: scoreboard);
        }

        [HttpGet("/team/{id:guid}")]
        public async Task<ContentResult> Team(Guid id)
        {
            var data = await context.Set<TournamentTeamEntity>()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.TeamName,
                    x.RequiresApproval,
                    MemberCount = x.Members.Count,
                    TournamentId = x.TournamentId,
                    TournamentName = x.Tournament != null ? x.Tournament.Name : null,
                    TeamSize = x.Tournament != null ? x.Tournament.TeamSize : 0,
                    HubName = x.Tournament != null && x.Tournament.Hub != null ? x.Tournament.Hub.Name : null,
                    HubAvatarUrl = x.Tournament != null && x.Tournament.Hub != null ? x.Tournament.Hub.AvatarUrl : null,
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFoundPage("Team", $"team/{id}");
            }

            var stats = new List<ShareStat>
            {
                new("Members", data.TeamSize > 0
                    ? $"{data.MemberCount}/{data.TeamSize}"
                    : $"{data.MemberCount}"),
                new("Joining", data.RequiresApproval ? "Approval required" : "Open"),
            };

            if (!string.IsNullOrWhiteSpace(data.TournamentName))
            {
                stats.Add(new("Tournament", data.TournamentName!));
            }

            string description = data.TournamentName != null
                ? $"Team in {data.TournamentName} · {data.MemberCount}"
                  + (data.TeamSize > 0 ? $"/{data.TeamSize}" : "")
                  + $" members · {(data.RequiresApproval ? "Approval required" : "Open to join")}"
                : $"{data.MemberCount}"
                  + (data.TeamSize > 0 ? $"/{data.TeamSize}" : "")
                  + $" members · {(data.RequiresApproval ? "Approval required" : "Open to join")} on {config.AppName}";

            return await SharePage(
                ShareEntityType.Team,
                "Team",
                webPath: $"team/{id}",
                deepPath: $"team/{id}",
                title: data.TeamName,
                description: description,
                imageUrl: data.HubAvatarUrl,
                entityId: id,
                stats: stats,
                contextText: data.HubName,
                contextImageUrl: data.HubAvatarUrl);
        }

        [HttpGet("/.well-known/apple-app-site-association")]
        public IActionResult AppleAppSiteAssociation()
        {
            if (string.IsNullOrWhiteSpace(config.AppleTeamId))
            {
                return NotFound();
            }

            var payload = new
            {
                applinks = new
                {
                    apps = Array.Empty<string>(),
                    details = new[]
                    {
                        new
                        {
                            appIDs = new[] { $"{config.AppleTeamId}.{config.IosBundleId}" },
                            paths = new[] { "/tournament/*", "/hub/*", "/user/*", "/team/*" },
                        },
                    },
                },
            };

            return Content(JsonSerializer.Serialize(payload), "application/json");
        }

        [HttpGet("/.well-known/assetlinks.json")]
        public IActionResult AndroidAssetLinks()
        {
            if (config.AndroidCertFingerprints.Length == 0)
            {
                return NotFound();
            }

            var payload = new[]
            {
                new
                {
                    relation = new[] { "delegate_permission/common.handle_all_urls" },
                    target = new
                    {
                        @namespace = "android_app",
                        package_name = config.AndroidPackageName,
                        sha256_cert_fingerprints = config.AndroidCertFingerprints,
                    },
                },
            };

            return Content(JsonSerializer.Serialize(payload), "application/json");
        }

        private async Task<ContentResult> SharePage(
            ShareEntityType entityType,
            string entityLabel,
            string webPath,
            string deepPath,
            string title,
            string description,
            string? imageUrl,
            Guid entityId,
            IReadOnlyList<ShareStat>? stats = null,
            string? contextText = null,
            string? contextImageUrl = null,
            bool compactStats = false,
            PlayerScoreboard? scoreboard = null)
        {
            await LogShare(entityType, entityId);

            var model = new SharePageModel
            {
                Title = title,
                Description = description,
                CanonicalUrl = $"{config.BaseUrl.TrimEnd('/')}/{webPath}",
                DeepLink = $"{config.AppScheme}://{deepPath}",
                EntityLabel = entityLabel,
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? config.DefaultImageUrl : imageUrl,
                AppName = config.AppName,
                AppStoreUrl = config.AppStoreUrl,
                PlayStoreUrl = config.PlayStoreUrl,
                Stats = stats,
                ContextText = contextText,
                ContextImageUrl = contextImageUrl,
                CompactStats = compactStats,
                Scoreboard = scoreboard,
            };

            Response.Headers.CacheControl = "public, max-age=300";

            return new ContentResult
            {
                Content = SharePageBuilder.BuildPage(model),
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status200OK,
            };
        }

        // Deliberately NOT logged to ShareLog: the entity doesn't exist, so the row would carry
        // zero analytics value while giving bots and dead links an unbounded write primitive.
        private ContentResult NotFoundPage(
            string entityLabel,
            string webPath)
        {
            var model = new SharePageModel
            {
                Title = $"{entityLabel} not available",
                Description = $"This {entityLabel.ToLowerInvariant()} no longer exists or the link is invalid. Get {config.AppName} to explore tournaments, hubs and players.",
                CanonicalUrl = $"{config.BaseUrl.TrimEnd('/')}/{webPath}",
                DeepLink = $"{config.AppScheme}://home",
                EntityLabel = entityLabel,
                ImageUrl = config.DefaultImageUrl,
                AppName = config.AppName,
                AppStoreUrl = config.AppStoreUrl,
                PlayStoreUrl = config.PlayStoreUrl,
            };

            return new ContentResult
            {
                Content = SharePageBuilder.BuildPage(model),
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status404NotFound,
            };
        }

        // Soft daily write cap per client IP. The Get+Set pair races under concurrency, but for
        // abuse capping "roughly N" is all we need — an atomic counter isn't worth extending
        // ICacheService. Real users share a link a handful of times a day; only bots hit this.
        private const int MaxShareLogsPerIpPerDay = 200;

        private async Task LogShare(ShareEntityType entityType, Guid entityId)
        {
            try
            {
                string ip = ResolveClientIp() ?? "unknown";
                string capKey = $"sharelog_ip:{ip}:{DateTime.UtcNow:yyyyMMdd}";
                int hitsToday = await cacheService.GetAsync<int>(capKey);
                if (hitsToday >= MaxShareLogsPerIpPerDay) return;
                await cacheService.SetAsync(capKey, hitsToday + 1, TimeSpan.FromHours(25));

                string userAgent = Request.Headers.UserAgent.ToString();

                context.Add(new ShareLogEntity
                {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    EntityType = entityType,
                    Platform = ShareBotDetector.DetectPlatform(userAgent),
                    IpAddress = ResolveClientIp(),
                    UserAgent = Truncate(userAgent, 512),
                    CreatedOn = DateTime.UtcNow,
                    ModifiedOn = DateTime.UtcNow,
                    IsDeleted = false,
                });

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The page must render even when analytics logging fails.
                logger.LogWarning(ex, "Failed to write ShareLog for {EntityType} {EntityId}", entityType, entityId);
            }
        }

        private string? ResolveClientIp()
        {
            string forwarded = Request.Headers["X-Forwarded-For"].ToString();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return Truncate(forwarded.Split(',')[0].Trim(), 64);
            }

            return Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), 64);
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string Humanize(string value)
        {
            return string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
        }

        private static string CurrencyLabel(PrizeCurrency currency)
        {
            return currency switch
            {
                PrizeCurrency.Eur => "EUR",
                PrizeCurrency.Dollar => "USD",
                PrizeCurrency.StarPass => "Star Pass",
                PrizeCurrency.FCP => "FCP",
                _ => currency.ToString(),
            };
        }
    }
}
