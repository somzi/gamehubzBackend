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

        [HttpGet("{id}/team/details")]
        public async Task<IActionResult> GetDetailsTeamMatch(Guid id)
        {
            var teamMatchDetails = await this.teamMatchService.GetTeamMatchDetails(id);
            var s = JsonSerializer.Serialize(teamMatchDetails);
            return Ok(teamMatchDetails);
        }
    }
}