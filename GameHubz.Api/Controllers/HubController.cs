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
        private readonly UserHubRequestService hubJoinRequestService;
        private readonly UserHubService userHubService;

        public HubController(
            HubService service,
            UserHubRequestService hubJoinRequestService,
            UserHubService userHubService)
        {
            this.hubService = service;
            this.hubJoinRequestService = hubJoinRequestService;
            this.userHubService = userHubService;
        }

        [HttpGet("getAll")]
        public async Task<IEnumerable<HubDto>> GetAll()
        {
            return await hubService.GetAll();
        }

        [HttpGet("{id}")]
        public async Task<HubOverviewDto> GetById(Guid id)
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
        public async Task<IEnumerable<HubDto>> GetUserJoinedHubs(Guid userId, [FromQuery] int pageNumber, [FromQuery] string? search = null)
        {
            return await hubService.GetJoinedByUser(userId, pageNumber, search);
        }

        [HttpGet("user/{userId}/discovery")]
        public async Task<IEnumerable<HubDto>> GetUserNotJoined(Guid userId, [FromQuery] int pageNumber, [FromQuery] string? search = null)
        {
            return await hubService.GetUserNotJoined(userId, pageNumber, search);
        }

        [HttpGet("{id}/members")]
        public async Task<IEnumerable<UserHubOverview>> GetMembers(Guid id)
        {
            return await hubService.GetMembers(id);
        }

        [HttpPost("{id}/members")]
        public async Task<UserHubDto> AddMember(Guid id, [FromBody] AddHubMemberRequest request)
        {
            return await userHubService.AddMember(id, request.UserId, request.Role);
        }

        [HttpPut("{id}/members/{userId}")]
        public async Task<UserHubDto> ChangeMemberRole(Guid id, Guid userId, [FromBody] ChangeHubMemberRoleRequest request)
        {
            return await userHubService.ChangeMemberRole(id, userId, request.Role);
        }

        [HttpDelete("{id}/members/{userId}")]
        public async Task RemoveMember(Guid id, Guid userId)
        {
            await userHubService.RemoveMember(id, userId);
        }

        [HttpPost("{id}/members/{userId}/ban")]
        public async Task BanMember(Guid id, Guid userId)
        {
            await userHubService.BanMember(id, userId);
        }

        [HttpPost("{id}/user/{userId}/kick")]
        public async Task KickMember(Guid id, Guid userid)
        {
            await hubService.KickUserFromHub(id, userid);
        }

        [HttpPost("{id}/avatar")]
        public async Task<IActionResult> UploadHubAvatar(Guid id, IFormFile avatar)
        {
            if (avatar == null || avatar.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }
            await hubService.UploadAvatar(id, avatar);

            return Ok("Avatar uploaded successfully.");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await hubService.DeleteEntity(id);

            return Ok();
        }

        [HttpPost("{id}/join-request")]
        public async Task<IActionResult> RequestJoin(Guid id)
        {
            await hubJoinRequestService.RequestJoin(id);
            return Ok();
        }

        [HttpDelete("{id}/join-request")]
        public async Task<IActionResult> CancelMyJoinRequest(Guid id)
        {
            await hubJoinRequestService.CancelMyRequest(id);
            return Ok();
        }

        [HttpGet("{id}/join-requests")]
        public async Task<IEnumerable<UserHubRequestDto>> GetJoinRequests(Guid id)
        {
            return await hubJoinRequestService.GetPendingRequests(id);
        }

        [HttpPost("join-request/{requestId}/approve")]
        public async Task<IActionResult> ApproveJoinRequest(Guid requestId)
        {
            await hubJoinRequestService.ApproveRequest(requestId);
            return Ok();
        }

        [HttpPost("join-request/{requestId}/reject")]
        public async Task<IActionResult> RejectJoinRequest(Guid requestId)
        {
            await hubJoinRequestService.RejectRequest(requestId);
            return Ok();
        }
    }
}