using GameHubz.Common.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TournamentParticipantController : BasicGenericController<TournamentParticipantService, TournamentParticipantEntity, TournamentParticipantDto, TournamentParticipantPost, TournamentParticipantEdit>
    {
        public TournamentParticipantController(
            TournamentParticipantService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        // The inherited generic POST/DELETE would let any user inject/remove a participant by id,
        // bypassing the registration-approval flow. Participants are materialized server-side by the
        // manager-authorized approval path, so lock the generic write endpoints to Admin. The
        // manager-checked RemoveUser/RemoveTeam below remain the supported removal path.
        protected override UserRoleEnum[]? UserRolesSave() => [UserRoleEnum.Admin];

        protected override UserRoleEnum[]? UserRolesDelete() => [UserRoleEnum.Admin];

        [HttpGet("tournament/{tournamentId}")]
        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            var participants = await this.Service.GetByTournament(tournamentId);

            return participants;
        }

        [HttpPost("tournament/{tournamentId}/user/{userId}")]
        public async Task RemoveUser(Guid tournamentId, Guid userId)
        {
            await this.Service.RemoveUser(tournamentId, userId);
        }

        [HttpPost("tournament/{tournamentId}/team/{teamId}")]
        public async Task<IActionResult> RemoveTeam(Guid tournamentId, Guid teamId)
        {
            await this.Service.RemoveTeam(tournamentId, teamId);

            return Ok();
        }

        // ── Participant swap ─────────────────────────────────────────────────────────────────────
        // New routes, so no /vN is needed: nothing an already-published app calls changes shape.
        // All three are manager-gated inside the service (TournamentAuthorizationService).

        /// <summary>Whether this participant may still be replaced, plus the played/total numbers behind it.</summary>
        [HttpGet("tournament/{tournamentId}/user/{userId}/swap-eligibility")]
        public async Task<ParticipantSwapEligibilityDto> GetSwapEligibility(Guid tournamentId, Guid userId)
        {
            return await this.Service.GetSwapEligibility(tournamentId, userId);
        }

        /// <summary>Hub members who can take over a spot, minus everyone already in the tournament.</summary>
        [HttpGet("tournament/{tournamentId}/swap-candidates")]
        public async Task<List<ParticipantSwapCandidateDto>> GetSwapCandidates(
            Guid tournamentId,
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 0)
        {
            return await this.Service.GetSwapCandidates(tournamentId, search, pageNumber);
        }

        /// <summary>Hands a participant's spot — seed, group, standings and played matches — to another member.</summary>
        [HttpPost("tournament/{tournamentId}/swap")]
        public async Task<ParticipantSwapResultDto> Swap(Guid tournamentId, [FromBody] ParticipantSwapRequest request)
        {
            return await this.Service.SwapParticipant(tournamentId, request);
        }
    }
}