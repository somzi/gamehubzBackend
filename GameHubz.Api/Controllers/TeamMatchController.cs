using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/team-matches")]
    [ApiController]
    [Authorize]
    public class TeamMatchController : ControllerBase
    {
        private readonly TeamMatchService teamMatchService;

        public TeamMatchController(TeamMatchService teamMatchService)
        {
            this.teamMatchService = teamMatchService;
        }

        [HttpPost("{teamMatchId}/tiebreak/representative")]
        public async Task<IActionResult> SubmitRepresentative(Guid teamMatchId, [FromBody] SubmitRepresentativeRequest request)
        {
            var result = await this.teamMatchService.SubmitRepresentative(teamMatchId, request);
            return Ok(result);
        }

        [HttpGet("{teamMatchId}/tiebreak/status")]
        public async Task<IActionResult> GetTieBreakStatus(Guid teamMatchId)
        {
            var result = await this.teamMatchService.GetTieBreakStatus(teamMatchId);
            return Ok(result);
        }
    }
}
