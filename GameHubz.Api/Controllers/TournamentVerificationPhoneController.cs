using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/tournament/{id}")]
    [ApiController]
    [Authorize]
    public class TournamentVerificationPhoneController : ControllerBase
    {
        private readonly TournamentVerificationPhoneService service;
        public TournamentVerificationPhoneController(TournamentVerificationPhoneService service) { this.service = service; }

        [HttpPost("verification-phone")]
        public async Task<IActionResult> Bind(Guid id, [FromBody] RegisterVerificationDeviceRequest request) =>
            Ok(await this.service.Bind(id, request));

        [HttpGet("verification-phones/pending")]
        public async Task<IActionResult> Pending(Guid id) => Ok(await this.service.GetPending(id));

        [HttpPost("verification-phones/{requestId}/approve")]
        public async Task<IActionResult> Approve(Guid id, Guid requestId, [FromBody] TournamentPhoneDecisionRequest request)
        {
            await this.service.Decide(id, requestId, request, approve: true);
            return Ok();
        }

        [HttpPost("verification-phones/{requestId}/reject")]
        public async Task<IActionResult> Reject(Guid id, Guid requestId, [FromBody] TournamentPhoneDecisionRequest request)
        {
            await this.service.Decide(id, requestId, request, approve: false);
            return Ok();
        }
    }
}
