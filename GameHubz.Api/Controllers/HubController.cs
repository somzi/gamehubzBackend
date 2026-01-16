using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
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
    }
}