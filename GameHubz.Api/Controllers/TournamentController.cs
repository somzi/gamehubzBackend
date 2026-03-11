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

        [HttpPost("createBracket")]
        public async Task<IActionResult> CreateBracket([FromBody] CreateBracketRequest request)
        {
            await this.bracketService.CreateBracket(request);

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

        [HttpGet("{id}/overview")]
        public async Task<IActionResult> GetOverview([FromRoute] Guid id)
        {
            var data = await this.Service.GetOverview(id);

            return Ok(data);
        }

        [HttpGet("{id}/user/{userId}/registred")]
        public async Task<IActionResult> CheckIsUserRegistred(Guid id, Guid userId)
        {
            var isUserAlreadyRegistred = await this.Service.CheckIsUserRegistred(id, userId);

            return Ok(isUserAlreadyRegistred);
        }

        [HttpPut("{id}/roundDeadline")]
        public async Task<IActionResult> SetRoundDeadline([FromRoute] Guid id, [FromBody] SetRoundDeadlineRequest request)
        {
            await this.Service.SetRoundDeadline(id, request.RoundNumber, request.Deadline);

            return Ok();
        }
    }
}