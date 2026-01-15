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
    public class TournamentGroupController : BasicGenericController<TournamentGroupService, TournamentGroupEntity, TournamentGroupDto, TournamentGroupPost, TournamentGroupEdit>
    {
        public TournamentGroupController(
            TournamentGroupService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }
    }
}