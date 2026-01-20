using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/match")]
    [ApiController]
    [Authorize]
    public class MatchController : BasicGenericController<MatchService, MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        public MatchController(
            MatchService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
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
    }
}