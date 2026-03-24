using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/teams")]
    [ApiController]
    [Authorize]
    public class TournamentTeamController : ControllerBase
    {
        private readonly TournamentTeamService tournamentTeamService;

        public TournamentTeamController(TournamentTeamService tournamentTeamService)
        {
            this.tournamentTeamService = tournamentTeamService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateTeam([FromBody] CreateTeamRequest request)
        {
            var team = await this.tournamentTeamService.CreateTeam(request);
            return Ok(team);
        }

        [HttpPut("{teamId}/name")]
        public async Task<IActionResult> RenameTeam(Guid teamId, [FromBody] RenameTeamRequest request)
        {
            var team = await this.tournamentTeamService.RenameTeam(teamId, request);
            return Ok(team);
        }

        [HttpDelete("{teamId}")]
        public async Task<IActionResult> DeleteTeam(Guid teamId)
        {
            await this.tournamentTeamService.DeleteTeam(teamId);
            return NoContent();
        }

        [HttpPost("{teamId}/join")]
        public async Task<IActionResult> JoinTeam(Guid teamId)
        {
            var team = await this.tournamentTeamService.JoinTeam(teamId);
            return Ok(team);
        }

        [HttpDelete("{teamId}/members/{userId}")]
        public async Task<IActionResult> KickMember(Guid teamId, Guid userId)
        {
            await this.tournamentTeamService.KickMember(teamId, userId);
            return NoContent();
        }

        [HttpDelete("{teamId}/leave")]
        public async Task<IActionResult> LeaveTeam(Guid teamId)
        {
            await this.tournamentTeamService.LeaveTeam(teamId);
            return NoContent();
        }
    }
}