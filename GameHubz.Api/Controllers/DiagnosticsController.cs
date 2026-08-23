using GameHubz.Api.Models;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Client-reported launch diagnostics. New surface with no legacy clients, so it is
    /// versioned at v2 from the start.
    /// </summary>
    [Route("api/v2/diagnostics")]
    [ApiController]
    [Authorize]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ApplicationContext db;

        public DiagnosticsController(ApplicationContext db)
        {
            this.db = db;
        }

        /// <summary>
        /// <c>expo-updates</c> fell back to the build's embedded bundle because a downloaded OTA
        /// update failed to boot. Nothing about that ever reaches the server on its own — the
        /// broken bundle never runs long enough to make a request — so the app reports it once
        /// per launch and we park it in the existing ErrorLog triage list.
        /// </summary>
        [HttpPost("emergency-launch")]
        public async Task<IActionResult> ReportEmergencyLaunch([FromBody] EmergencyLaunchReport? report)
        {
            DateTime now = DateTime.UtcNow;

            // Written straight through ApplicationContext, mirroring
            // ExceptionHandlingMiddlware.TryPersistErrorLog: ErrorLog has no repository and
            // introducing one for a single insert is more plumbing than the row is worth.
            // Same table on purpose — an OTA update that bricks the app belongs in the list
            // you already triage, not in a second place you have to remember to check.
            var entity = new ErrorLogEntity
            {
                Id = Guid.NewGuid(),
                CreatedOn = now,
                ModifiedOn = now,
                IsDeleted = false,
                UserId = TryGetUserId(),
                Category = "ClientEmergencyLaunch",
                ExceptionType = "EmergencyLaunch",
                Message = Truncate(report?.Reason, 4000)
                    ?? "expo-updates emergency launch (no reason reported)",
                StackTrace = Truncate(DescribeClientUpdateState(report), 4000),

                // Not a failed request — the call itself succeeded. 200 keeps these rows from
                // polluting any "what is 5xx-ing" filter over ErrorLog.
                StatusCode = StatusCodes.Status200OK,
                RequestMethod = Request.Method,
                RequestPath = Request.Path.ToString(),
                UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 1024),
                AppVersion = Truncate(GetHeader("X-App-Version"), 64),
                Platform = Truncate(GetHeader("X-Platform"), 64),
                IpAddress = Truncate(ResolveClientIp(), 64),
                IsResolved = false,
            };

            this.db.Set<ErrorLogEntity>().Add(entity);
            await this.db.SaveChangesAsync();

            return Ok(new { errorId = entity.Id });
        }

        private static string DescribeClientUpdateState(EmergencyLaunchReport? report)
            => $"updateId={report?.UpdateId ?? "(none)"}; "
             + $"runtimeVersion={report?.RuntimeVersion ?? "(none)"}; "
             + $"channel={report?.Channel ?? "(none)"}";

        private Guid? TryGetUserId()
        {
            string? rawId = User.Identities
                .SelectMany(identity => identity.Claims)
                .FirstOrDefault(claim => claim.Type.Equals("id", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return Guid.TryParse(rawId, out Guid userId) ? userId : null;
        }

        private string? GetHeader(string name)
            => Request.Headers.TryGetValue(name, out var value) && value.Count > 0
                ? value.ToString()
                : null;

        /// <summary>
        /// The API runs behind a reverse proxy in Docker, so <c>Connection.RemoteIpAddress</c> is
        /// always the proxy container IP — the real client address is the leftmost entry of
        /// <c>X-Forwarded-For</c>.
        /// </summary>
        private string? ResolveClientIp()
        {
            string forwarded = Request.Headers["X-Forwarded-For"].ToString();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
