using GameHubz.Common.Models;
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

        // A match is never a plain CRUD row. Every legitimate mutation goes through a domain method
        // that holds the advancement advisory lock, runs the approval / check-in rules and cascades
        // into the bracket. The generic routes inherited from BasicGenericController bypassed all of
        // that: its UserRolesSave/Delete default to null and AppAuthorizationService treats null as
        // "no role required", so POST api/match with an existing Id (StageEntity keys insert-vs-update
        // off Id.HasValue) let any signed-in user rewrite any match's score, status and winner, and
        // DELETE api/match/{id} let them drop it. The reads leaked every match in the platform with
        // arbitrary filters. Nothing calls them — the mobile client only uses the named sub-routes
        // below — so they are removed from routing outright rather than merely role-gated.
        [NonAction]
        public override Task Delete(Guid id)
            => throw new NotSupportedException("Matches are deleted through tournament/bracket operations.");

        [NonAction]
        public override Task<MatchDto> GetById(Guid id)
            => throw new NotSupportedException("Use GET api/match/{id}/details.");

        [NonAction]
        public override Task<EntityListDto<MatchDto>> GetList(
            int? pageIndex,
            int? pageSize,
            List<SortItem> sortItems,
            List<FilterItem> filterItems)
            => throw new NotSupportedException("Matches are listed through the tournament structure endpoints.");

        [NonAction]
        public override Task<MatchDto> SaveEntity(MatchPost modelSave)
            => throw new NotSupportedException("Match results are submitted through BracketService (see MatchResultController).");

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

        // Kestrel's 30MB default was sized for a batch of screenshots and would reject a request
        // carrying a clip alongside them. Raised only here, rather than globally, so this stays
        // the one endpoint in the API that may accept a body this large. Per-file limits are
        // enforced in the storage layer (20MB an image, 32MB a video); this is the envelope.
        [RequestSizeLimit(60 * 1024 * 1024)]
        [HttpPost("{id}/evidence")]
        public async Task<IActionResult> UploadEvidence(Guid id, List<IFormFile> files)
        {
            await this.Service.UploadMatchEvidence(id, files);
            return Ok(new { message = "Evidence uploaded successfully" });
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

        // Combined payload for the mobile match modal — bundles details + streams + the caller's
        // availability (when the match is still in play) so opening a match only costs one round
        // trip instead of three. The Details field is polymorphic (solo details vs team-match
        // details), mirroring the /details endpoint above.
        //
        // The match entity is loaded ONCE at the top and its TeamMatchId + Status are passed
        // downstream, avoiding the two redundant ShallowGetById calls the earlier draft did (one
        // inside GetTeamMatchRef and one inside GetStreamsAndAvailability). Task.WhenAll is
        // deliberately NOT used here — the repositories share a single DbContext and EF Core
        // throws on concurrent operations against the same context.
        [HttpGet("{id}/details/full")]
        public async Task<IActionResult> GetDetailsFull(Guid id)
        {
            var match = await this.Service.GetMatchEntityById(id);
            if (match == null) return NotFound(new { message = "Match not found" });

            object details = match.TeamMatchId.HasValue
                ? await this.teamMatchService.GetTeamMatchDetails(match.TeamMatchId.Value)
                : await this.Service.GetWithEvidence(id);

            var (streams, availability, adminAvailability) =
                await this.Service.GetStreamsAndAvailability(id, match.Status, match.TournamentId);

            return Ok(new MatchDetailsFullDto
            {
                Details = details,
                Streams = streams,
                Availability = availability,
                AdminAvailability = adminAvailability,
            });
        }

        [HttpPost("{id}/schedule")]
        public async Task<IActionResult> SetScheduled(Guid id)
        {
            await this.Service.SetScheduled(id);
            return Ok(new { message = "Match scheduled successfully." });
        }

        // Organizer-only: drops the confirmed kick-off AND both sides' availability, putting the
        // match back to Pending so the two can schedule again. Authorization and the state check
        // live in the service.
        [HttpPost("{id}/schedule/clear")]
        public async Task<IActionResult> ClearSchedule(Guid id)
        {
            await this.Service.ClearSchedule(id);
            return Ok(new { message = "Scheduled time cleared." });
        }

        // Ready check: "I am here" for the caller's side of a scheduled match. Idempotent, and
        // the service owns every rule (check enabled, window open, caller on one of the sides).
        [HttpPost("{id}/checkin")]
        public async Task<IActionResult> CheckIn(Guid id)
        {
            var result = await this.Service.CheckIn(id);
            return Ok(result);
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

        [HttpGet("pendingApproval/tournament/{tournamentId}")]
        public async Task<List<MatchPendingApprovalItemDto>> GetPendingApprovalMatches(Guid tournamentId)
        {
            return await this.Service.GetPendingApprovalMatches(tournamentId);
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