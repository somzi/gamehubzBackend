using GameHubz.Common.Models;
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
    public class HubActivityController : BasicGenericController<HubActivityService, HubActivityEntity, HubActivityDto, HubActivityPost, HubActivityEdit>
    {
        public HubActivityController(
            HubActivityService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        // Activity rows are produced internally by domain services. Exposing the inherited generic
        // CRUD would let any authenticated caller forge, enumerate or delete feed entries.
        [NonAction]
        public override Task Delete(Guid id)
            => throw new NotSupportedException("Hub activities are managed internally.");

        [NonAction]
        public override Task<HubActivityDto> GetById(Guid id)
            => throw new NotSupportedException("Use the home or all activity feed endpoints.");

        [NonAction]
        public override Task<EntityListDto<HubActivityDto>> GetList(
            int? pageIndex,
            int? pageSize,
            List<SortItem> sortItems,
            List<FilterItem> filterItems)
            => throw new NotSupportedException("Use the home or all activity feed endpoints.");

        [NonAction]
        public override Task<HubActivityDto> SaveEntity(HubActivityPost modelSave)
            => throw new NotSupportedException("Hub activities are managed internally.");

        [HttpGet("home")]
        public async Task<IActionResult> GetDashboardHighlights()
        {
            var highlights = await this.Service.GetDashboardHighlights();

            return Ok(highlights);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllDashboardHighlights([FromQuery] int pageNumber)
        {
            var highlights = await this.Service.GetAllDashboardHighlights(pageNumber);

            return Ok(highlights);
        }
    }
}
