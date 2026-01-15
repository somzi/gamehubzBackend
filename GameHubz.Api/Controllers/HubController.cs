using GameHubz.Common.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
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
        public async Task<IEnumerable<HubDto>> GetAll(Guid id)
        {
            return await hubService.GetById(id);
        }
    }
}