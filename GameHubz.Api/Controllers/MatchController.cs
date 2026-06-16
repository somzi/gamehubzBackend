using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GameHubz.Api.Controllers
{
    [Route("api/match")]
    [ApiController]
    [Authorize]
    public class MatchController : BasicGenericController<MatchService, MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly TeamMatchService teamMatchService;

        public MatchController(
            MatchService service,
            AppAuthorizationService appAuthorizationService,
            TeamMatchService teamMatchService)
            : base(service, appAuthorizationService)
        {
            this.teamMatchService = teamMatchService;
        }

        [HttpPost("availability")]
        public async Task<IActionResult> SubmitAvailability([FromBody] SubmitAvailabilityRequest request)
        {
            var result = await this.Service.SetAvailability(request.MatchId, request.SelectedSlots);

            if (result.ConfirmedTime.HasValue)
            {
                return Ok(new { Message = "Match Scheduled!", Data = result });
            }

            return Ok(new { Message = "Availability Saved. Waiting for opponent.", Data = result });
        }

        [HttpGet("home/{userId}")]
        public async Task<List<MatchOverviewDto>> GetMatchesByUser(Guid userId)
        {
            var matches = await this.Service.GetByUser(userId);
            return matches;
        }

        [HttpGet("{id}/availability/user/{userId}")]
        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            var matchAvailabilityDto = await this.Service.GetAvailability(id, userId);
            return matchAvailabilityDto;
        }

        [HttpPost("{id}/evidence")]
        public async Task<IActionResult> UploadEvidence(Guid id, List<IFormFile> files)
        {
            await this.Service.UploadMatchEvidence(id, files);
            return Ok(new { message = "Screenshot uploaded successfully" });
        }

        [HttpGet("{id}/details")]
        public async Task<IActionResult> GetDetails(Guid id)
        {
            var match = await this.Service.GetMatchEntityById(id);

            if (match?.TeamMatchId.HasValue == true)
            {
                var teamMatchDetails = await this.teamMatchService.GetTeamMatchDetails(match.TeamMatchId.Value);
                return Ok(teamMatchDetails);
            }

            var result = await this.Service.GetWithEvidence(id);
            return Ok(result);
        }

        [HttpPost("{id}/schedule")]
        public async Task<IActionResult> SetScheduled(Guid id)
        {
            await this.Service.SetScheduled(id);
            return Ok(new { message = "Match scheduled successfully." });
        }

        [HttpPost("{id}/adminHelp")]
        public async Task<IActionResult> RequestAdminHelp(Guid id)
        {
            await this.Service.RequestAdminHelp(id);
            return Ok(new { message = "Tournament admins have been notified." });
        }

        [HttpPost("{id}/adminHelp/resolve")]
        public async Task<IActionResult> ResolveAdminHelp(Guid id)
        {
            await this.Service.ResolveAdminHelp(id);
            return Ok(new { message = "Help request resolved." });
        }

        [HttpGet("adminHelp/tournament/{tournamentId}")]
        public async Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId)
        {
            return await this.Service.GetAdminHelpRequests(tournamentId);
        }

        [HttpGet("{id}/team/details")]
        public async Task<IActionResult> GetDetailsTeamMatch(Guid id)
        {
            var teamMatchDetails = await this.teamMatchService.GetTeamMatchDetails(id);
            var s = JsonSerializer.Serialize(teamMatchDetails);
            return Ok(teamMatchDetails);
        }

        // ─── Match streaming ────────────────────────────────────────────────

        // Latest stream for a match (null when there is none). Kept for back-compat.
        [HttpGet("{id}/stream")]
        public async Task<IActionResult> GetStream(Guid id)
        {
            var stream = await this.Service.GetStream(id);
            return Ok(stream);
        }

        // All current streams for a match (latest per streamer) — both opponents can stream at once.
        [HttpGet("{id}/streams")]
        public async Task<IActionResult> GetStreams(Guid id)
        {
            var streams = await this.Service.GetStreams(id);
            return Ok(streams);
        }

        // One-tap "I'm streaming this match" — sets the stream Live and saves the channel to socials.
        [HttpPost("{id}/stream/start")]
        public async Task<IActionResult> StartStream(Guid id, [FromBody] StartMatchStreamRequest request)
        {
            var stream = await this.Service.StartStream(id, request);
            return Ok(stream);
        }

        // Streamer stops — marks Ended and auto-resolves the VOD link (manual VodUrl overrides).
        [HttpPost("{id}/stream/end")]
        public async Task<IActionResult> EndStream(Guid id, [FromBody] EndMatchStreamRequest? request)
        {
            var stream = await this.Service.EndStream(id, request);
            return Ok(stream);
        }

        // Manual VOD fallback (mainly Kick) / correction.
        [HttpPost("{id}/stream/vod")]
        public async Task<IActionResult> SetStreamVod(Guid id, [FromBody] SetMatchStreamVodRequest request)
        {
            var stream = await this.Service.SetStreamVod(id, request);
            return Ok(stream);
        }

        // Soft-deletes the caller's stream (admins can pass streamerUserId to delete another's).
        // Used when the streamer attached the wrong channel or ended by mistake and wants to start fresh.
        [HttpDelete("{id}/stream")]
        public async Task<IActionResult> DeleteStream(Guid id, [FromQuery] Guid? streamerUserId)
        {
            await this.Service.DeleteStream(id, streamerUserId);
            return Ok(new { message = "Stream deleted" });
        }
    }
}