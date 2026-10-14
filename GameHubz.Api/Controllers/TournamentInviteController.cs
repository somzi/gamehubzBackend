using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Tournament invites. A new surface (no shipped client calls it), so it is versioned at v2 from
    /// the start, like the notification inbox. Organizer routes are manager-checked in the service;
    /// answering is limited to the invited player.
    /// </summary>
    [ApiController]
    [Authorize]
    public class TournamentInviteController : ControllerBase
    {
        private readonly TournamentInviteService inviteService;

        public TournamentInviteController(TournamentInviteService inviteService)
        {
            this.inviteService = inviteService;
        }

        /// <summary>
        /// Players the organizer can invite. No search = the hub's members; otherwise everyone whose
        /// username matches. <paramref name="onlyEligible"/> keeps only players inside the tournament's
        /// country / region / exclusive scope (the rest can still be invited as guests).
        /// </summary>
        [HttpGet("api/v2/tournaments/{tournamentId:guid}/invite-candidates")]
        public async Task<IActionResult> GetCandidates(
            Guid tournamentId,
            [FromQuery] string? search = null,
            [FromQuery] bool onlyEligible = false,
            [FromQuery] int pageNumber = 0)
        {
            return Ok(await this.inviteService.GetCandidates(tournamentId, search, onlyEligible, pageNumber));
        }

        /// <summary>Every invite of the tournament with its current status — the organizer's list.</summary>
        [HttpGet("api/v2/tournaments/{tournamentId:guid}/invites")]
        public async Task<IActionResult> GetTournamentInvites(Guid tournamentId)
        {
            return Ok(await this.inviteService.GetTournamentInvites(tournamentId));
        }

        /// <summary>Invites players into the tournament; one outcome per player.</summary>
        [HttpPost("api/v2/tournaments/{tournamentId:guid}/invites")]
        public async Task<IActionResult> SendInvites(Guid tournamentId, [FromBody] SendTournamentInvitesRequest request)
        {
            return Ok(await this.inviteService.SendInvites(tournamentId, request));
        }

        /// <summary>The signed-in player's latest invite to this tournament, or null.</summary>
        [HttpGet("api/v2/tournaments/{tournamentId:guid}/my-invite")]
        public async Task<IActionResult> GetMyInvite(Guid tournamentId)
        {
            return Ok(await this.inviteService.GetMyInvite(tournamentId));
        }

        [HttpPost("api/v2/tournament-invites/{inviteId:guid}/accept")]
        public async Task<IActionResult> Accept(Guid inviteId)
        {
            await this.inviteService.AcceptInvite(inviteId);
            return Ok();
        }

        [HttpPost("api/v2/tournament-invites/{inviteId:guid}/decline")]
        public async Task<IActionResult> Decline(Guid inviteId)
        {
            await this.inviteService.DeclineInvite(inviteId);
            return Ok();
        }

        /// <summary>The organizer withdraws an invite that hasn't been answered yet.</summary>
        [HttpPost("api/v2/tournament-invites/{inviteId:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid inviteId)
        {
            await this.inviteService.CancelInvite(inviteId);
            return Ok();
        }
    }
}
