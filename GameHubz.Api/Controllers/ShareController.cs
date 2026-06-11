using GameHubz.Api.Share;
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
    /// a store-link fallback. Every hit is recorded in ShareLog.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class ShareController : ControllerBase
    {
        private readonly ApplicationContext context;
        private readonly ShareLinksConfig config;
        private readonly ILogger<ShareController> logger;

        public ShareController(
            ApplicationContext context,
            IOptions<ShareLinksConfig> options,
            ILogger<ShareController> logger)
        {
            this.context = context;
            this.config = options.Value;
            this.logger = logger;
        }

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
                    HubAvatarUrl = x.Hub != null ? x.Hub.AvatarUrl : null,
                    ApprovedCount = x.TournamentRegistrations!
                        .Count(r => r.Status == TournamentRegistrationStatus.Approved),
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return await NotFoundPage(ShareEntityType.Tournament, "Tournament", $"tournament/{id}", id);
            }

            var parts = new List<string> { Humanize(data.Format.ToString()) };

            if (data.IsTeamTournament)
            {
                parts.Add(data.TeamSize > 0 ? $"{data.TeamSize}v{data.TeamSize} teams" : "Team tournament");
            }
            else
            {
                parts.Add(data.MaxPlayers > 0
                    ? $"{data.ApprovedCount}/{data.MaxPlayers} players"
                    : $"{data.ApprovedCount} players");
            }

            if (data.Prize > 0)
            {
                parts.Add($"Prize {data.Prize} {CurrencyLabel(data.PrizeCurrency)}");
            }

            if (data.StartDate.HasValue)
            {
                parts.Add($"Starts {data.StartDate.Value:MMM d, yyyy}");
            }

            return await SharePage(
                ShareEntityType.Tournament,
                "Tournament",
                webPath: $"tournament/{id}",
                deepPath: $"tournament/{id}",
                title: data.Name,
                description: string.Join(" · ", parts),
                imageUrl: data.HubAvatarUrl,
                entityId: id);
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
                    TournamentCount = x.Tournaments!.Count(),
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return await NotFoundPage(ShareEntityType.Hub, "Hub", $"hub/{id}", id);
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
                return await NotFoundPage(ShareEntityType.User, "Player", $"user/{id}", id);
            }

            string displayName = string.IsNullOrWhiteSpace(data.Nickname) ? data.Username : data.Nickname!;

            return await SharePage(
                ShareEntityType.User,
                "Player",
                webPath: $"user/{id}",
                // The in-app route for a user profile is player/:id, not user/:id.
                deepPath: $"player/{id}",
                title: displayName,
                description: $"Player profile on {config.AppName} — tournaments, match history and stats.",
                imageUrl: data.AvatarUrl,
                entityId: id);
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
                            paths = new[] { "/tournament/*", "/hub/*", "/user/*" },
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
            Guid entityId)
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
            };

            Response.Headers.CacheControl = "public, max-age=300";

            return new ContentResult
            {
                Content = SharePageBuilder.BuildPage(model),
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status200OK,
            };
        }

        private async Task<ContentResult> NotFoundPage(
            ShareEntityType entityType,
            string entityLabel,
            string webPath,
            Guid entityId)
        {
            await LogShare(entityType, entityId);

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

        private async Task LogShare(ShareEntityType entityType, Guid entityId)
        {
            try
            {
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
