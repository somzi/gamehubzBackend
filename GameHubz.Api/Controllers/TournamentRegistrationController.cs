using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/tournamentRegistration")]
    [ApiController]
    [Authorize]
    public class TournamentRegistrationController : BasicGenericController<TournamentRegistrationService, TournamentRegistrationEntity, TournamentRegistrationDto, TournamentRegistrationPost, TournamentRegistrationEdit>
    {
        public TournamentRegistrationController(
            TournamentRegistrationService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        [HttpPost("approve")]
        public async Task<ActionResult> ApproveRegistration([FromBody] Guid registrationId)
        {
            await this.Service.ApproveRegistration(registrationId);

            return Ok();
        }

        [HttpPost("approveAll")]
        public async Task<IActionResult> ApproveRegistrations([FromBody] List<Guid> registrationIds)
        {
            await this.Service.ApproveRegistrations(registrationIds);

            return Ok();
        }

        [HttpPost("reject")]
        public async Task<IActionResult> ApproveRegistrations([FromBody] Guid registrationId)
        {
            await this.Service.RejectRegistration(registrationId);

            return Ok();
        }

        [HttpGet("tournament/{tournamentId}/pending")]
        public async Task<List<TournamentRegistrationOverview>> GetPendingByTournamentId(Guid tournamentId)
        {
            return await this.Service.GetPendingByTournamentId(tournamentId);
        }

        [HttpGet("tournament/{tournamentId}/team/{teamId}/register")]
        public async Task<IActionResult> RegisterTeam(Guid tournamentId, Guid teamId)
        {
            await this.Service.RegisterTeam(tournamentId, teamId);

            return Ok();
        }
    }
}