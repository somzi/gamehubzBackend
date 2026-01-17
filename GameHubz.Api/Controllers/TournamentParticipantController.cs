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

        [HttpGet("tournament/{tournamentId}")]
        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            var participants = await this.Service.GetByTournament(tournamentId);

            return participants;
        }
    }
}