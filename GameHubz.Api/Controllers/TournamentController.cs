using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/tournament")]
    [ApiController]
    [Authorize]
    public class TournamentController : BasicGenericController<TournamentService, TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        private readonly BracketService bracketService;

        public TournamentController(
            TournamentService service,
            AppAuthorizationService appAuthorizationService,
            BracketService bracketService)
            : base(service, appAuthorizationService)
        {
            this.bracketService = bracketService;
        }

        [HttpPost("{tournamentId}/createBracket")]
        public async Task<IActionResult> CreateBracket(Guid tournamentId)
        {
            await this.bracketService.CreateBracket(tournamentId);

            return Ok();
        }

        [HttpGet("{tournamentId}/structure")]
        public async Task<IActionResult> GetTournamentStructure(Guid tournamentId)
        {
            var structure = await this.bracketService.GetTournamentStructure(tournamentId);

            return Ok(structure);
        }

        [HttpPost("matchResult")]
        public async Task<IActionResult> UpdateMatchResult([FromBody] MatchResultDto request)
        {
            await this.bracketService.UpdateMatchResult(request);

            return Ok();
        }

        [HttpGet("{id}/details")]
        public async Task<IActionResult> GetDetails(Guid id)
        {
            var tournament = await this.Service.GetDetailsById(id);

            return Ok(tournament);
        }

        [HttpPost("{id}/closeRegistration")]
        public async Task<IActionResult> CloseRegistration([FromRoute] Guid id)
        {
            await this.Service.CloseRegistration(id);

            return Ok();
        }

        [HttpPost("{id}/publish")]
        public async Task<IActionResult> Publish([FromRoute] Guid id)
        {
            await this.Service.Publish(id);

            return Ok();
        }
    }
}