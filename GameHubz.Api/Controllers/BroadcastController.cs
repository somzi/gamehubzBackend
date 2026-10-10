using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Broadcast from the GameHubz app to the player's linked Twitch / Kick channel.
    ///
    /// prepare / stop are called by the app as the logged-in player. started / heartbeat / stopped are
    /// called by the phone's screen-capture process (iOS broadcast extension, Android service), which has
    /// no login: it sends the session token prepare handed out, in the X-Broadcast-Token header (headers
    /// are never written to the error log, request bodies are).
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class BroadcastController : ControllerBase
    {
        private const string TokenHeader = "X-Broadcast-Token";

        private readonly StreamBroadcastService streamBroadcastService;

        public BroadcastController(StreamBroadcastService streamBroadcastService)
        {
            this.streamBroadcastService = streamBroadcastService;
        }

        [HttpPost("match/{matchId}/prepare")]
        public async Task<IActionResult> Prepare(Guid matchId, [FromBody] PrepareBroadcastRequest request)
        {
            return Ok(await streamBroadcastService.Prepare(matchId, request));
        }

        [HttpPost("match/{matchId}/stop")]
        public async Task<IActionResult> Stop(Guid matchId)
        {
            await streamBroadcastService.StopFromApp(matchId);
            return Ok();
        }

        [AllowAnonymous]
        [HttpPost("started")]
        public async Task<IActionResult> Started([FromHeader(Name = TokenHeader)] string? token)
        {
            return Ok(await streamBroadcastService.Started(token));
        }

        [AllowAnonymous]
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromHeader(Name = TokenHeader)] string? token)
        {
            return Ok(await streamBroadcastService.Heartbeat(token));
        }

        [AllowAnonymous]
        [HttpPost("stopped")]
        public async Task<IActionResult> Stopped([FromHeader(Name = TokenHeader)] string? token)
        {
            await streamBroadcastService.Stopped(token);
            return Ok();
        }
    }
}
