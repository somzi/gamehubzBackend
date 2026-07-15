using GameHubz.DataModels.Config;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Discord integration (phase 2): OAuth account linking + DM preference, and the interactions
    /// webhook Discord calls for slash commands. Link status is not served here — it rides along on
    /// the existing GET /api/UserProfile/{id}/info payload.
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class DiscordController : ControllerBase
    {
        private readonly DiscordLinkService discordLinkService;
        private readonly DiscordInteractionsService discordInteractionsService;
        private readonly DiscordConfig config;

        public DiscordController(
            DiscordLinkService discordLinkService,
            DiscordInteractionsService discordInteractionsService,
            IOptions<DiscordConfig> discordOptions)
        {
            this.discordLinkService = discordLinkService;
            this.discordInteractionsService = discordInteractionsService;
            this.config = discordOptions.Value;
        }

        [HttpGet("link/start")]
        public async Task<IActionResult> StartLink()
        {
            var authorizeUrl = await discordLinkService.StartLinkAsync();
            return Ok(new { authorizeUrl });
        }

        // Anonymous by necessity: the system browser lands here from Discord without our JWT.
        // The one-time state (validated inside) is what ties the request to a GameHubz account.
        [AllowAnonymous]
        [HttpGet("link/callback")]
        public async Task<ContentResult> LinkCallback([FromQuery] string? code, [FromQuery] string? state)
        {
            var html = await discordLinkService.HandleCallbackAsync(code, state);
            return Content(html, "text/html");
        }

        [HttpDelete("link")]
        public async Task<IActionResult> Unlink()
        {
            await discordLinkService.UnlinkAsync();
            return Ok();
        }

        [HttpPut("dm-enabled")]
        public async Task<IActionResult> SetDmEnabled([FromBody] SetDiscordDmEnabledRequest request)
        {
            await discordLinkService.SetDmEnabledAsync(request.Enabled);
            return Ok();
        }

        [HttpPut("show-on-profile")]
        public async Task<IActionResult> SetShowOnProfile([FromBody] SetDiscordShowOnProfileRequest request)
        {
            await discordLinkService.SetShowOnProfileAsync(request.Show);
            return Ok();
        }

        /// <summary>
        /// Discord's Interactions Endpoint URL. The Ed25519 signature must be verified over the RAW
        /// body before anything is deserialized, so this action does its own body reading and skips
        /// model binding entirely. Invalid signature → 401 (Discord probes exactly this when the
        /// endpoint URL is saved; the valid probe is a PING answered with PONG inside the service).
        /// </summary>
        [AllowAnonymous]
        [HttpPost("interactions")]
        public async Task<IActionResult> Interactions()
        {
            string? signature = Request.Headers["X-Signature-Ed25519"].FirstOrDefault();
            string? timestamp = Request.Headers["X-Signature-Timestamp"].FirstOrDefault();

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (!DiscordSignatureVerifier.Verify(config.PublicKey, signature, timestamp, rawBody))
                return Unauthorized();

            var response = await discordInteractionsService.HandleAsync(rawBody);
            return Ok(response);
        }
    }
}
