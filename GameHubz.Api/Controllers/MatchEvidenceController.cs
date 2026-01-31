using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class MatchEvidenceController: BasicGenericController<MatchEvidenceService, MatchEvidenceEntity, MatchEvidenceDto, MatchEvidencePost, MatchEvidenceEdit>
    {
        public MatchEvidenceController(
            MatchEvidenceService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }
    }
}