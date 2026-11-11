using GameHubz.DataModels.Enums;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Linked Twitch / Kick channels for the in-app broadcast. The OAuth runs on the backend (same shape
    /// as the Discord link): the app opens authorizeUrl in the system browser and comes back through the
    /// gamehubz:// scheme. {platform} is "twitch" or "kick".
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class StreamAccountController : ControllerBase
    {
        private readonly StreamAccountService streamAccountService;

        public StreamAccountController(StreamAccountService streamAccountService)
        {
            this.streamAccountService = streamAccountService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMine()
        {
            return Ok(await streamAccountService.GetMine());
        }

        [HttpGet("{platform}/link/start")]
        public async Task<IActionResult> StartLink(string platform)
        {
            if (ParsePlatform(platform) is not SocialType parsed) return BadRequest();

            var authorizeUrl = await streamAccountService.StartLinkAsync(parsed);
            return Ok(new { authorizeUrl });
        }

        // Anonymous by necessity: the system browser lands here from Twitch / Kick without our JWT.
        // The one-time state (validated inside) is what ties the request to a GameHubz account.
        [AllowAnonymous]
        [HttpGet("{platform}/link/callback")]
        public async Task<ContentResult> LinkCallback(
            string platform,
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error)
        {
            if (ParsePlatform(platform) is not SocialType parsed)
                return Content("Unknown platform.", "text/plain");

            var html = await streamAccountService.HandleCallbackAsync(parsed, code, state, error);
            return Content(html, "text/html");
        }

        [HttpDelete("{platform}")]
        public async Task<IActionResult> Unlink(string platform)
        {
            if (ParsePlatform(platform) is not SocialType parsed) return BadRequest();

            await streamAccountService.UnlinkAsync(parsed);
            return Ok();
        }

        internal static SocialType? ParsePlatform(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "twitch" => SocialType.Twitch,
                "kick" => SocialType.Kick,
                _ => null,
            };
        }
    }
}
