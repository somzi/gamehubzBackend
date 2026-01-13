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
        public TournamentController(
            TournamentService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }
    }
}