using GameHubz.Common.Consts;
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

        // F105: the inherited generic POST/DELETE let any user forge or delete evidence on any match by
        // supplying a row Id. Legitimate evidence is created via the participant-checked
        // MatchService.UploadMatchEvidence endpoint, so lock the generic write paths to Admin.
        protected override UserRoleEnum[]? UserRolesSave() => [UserRoleEnum.Admin];

        protected override UserRoleEnum[]? UserRolesDelete() => [UserRoleEnum.Admin];
    }
}