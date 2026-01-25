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

        [HttpGet("home")]
        public async Task<IActionResult> GetDashboardHighlights()
        {
            var highlights = await this.Service.GetDashboardHighlights();

            return Ok(highlights);
        }
    }
}