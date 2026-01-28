using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class HubController : ControllerBase
    {
        private readonly HubService hubService;

        public HubController(
            HubService service)
        {
            this.hubService = service;
        }

        [HttpGet("getAll")]
        public async Task<IEnumerable<HubDto>> GetAll()
        {
            return await hubService.GetAll();
        }

        [HttpGet("{id}")]
        public async Task<HubOverviewDto> GetAll(Guid id)
        {
            return await hubService.GetOverviewById(id);
        }

        [HttpGet("{id}/tournaments")]
        public async Task<IActionResult> GetByHubPaged([FromRoute] Guid id, [FromQuery] TournamentRequest request)
        {
            var result = await hubService.GetTournamentsPaged(id, request);

            return Ok(result);
        }

        [HttpGet("user/{id}")]
        public async Task<IEnumerable<HubOverviewDto>> GetByUserOwner([FromRoute] Guid id)
        {
            List<HubOverviewDto> result = await hubService.GetByUserOwner(id);

            return result;
        }

        [HttpPost("update")]
        public async Task<HubOverviewDto> Update([FromBody] HubPost request)
        {
            HubOverviewDto result = await hubService.UpdateDetails(request);

            return result;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] HubPost request)
        {
            await hubService.Create(request);

            return Ok();
        }

        [HttpGet("user/{userId}/joined")]
        public async Task<IEnumerable<HubDto>> GetUserJoinedHubs(Guid userId)
        {
            return await hubService.GetJoinedByUser(userId);
        }

        [HttpGet("user/{userId}/discovery")]
        public async Task<IEnumerable<HubDto>> GetUserNotJoined(Guid userId)
        {
            return await hubService.GetUserNotJoined(userId);
        }
    }
}