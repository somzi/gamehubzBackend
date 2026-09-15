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
    public class MatchChatController : BasicGenericController<MatchChatService, MatchChatEntity, MatchChatDto, MatchChatPost, MatchChatEdit>
    {
        private readonly MatchChatService chatService;

        public MatchChatController(
            MatchChatService service,
            AppAuthorizationService appAuthorizationService,
            MatchChatService chatService)
            : base(service, appAuthorizationService)
        {
            this.chatService = chatService;
        }

        // Same reasoning as MatchController: the inherited generic CRUD carried no role requirement
        // (UserRolesSave/Delete default to null, which AppAuthorizationService treats as "allow"), so
        // any signed-in user could post a message as another user, edit one by Id, delete any message
        // and page through every chat line on the platform — all bypassing the participant check that
        // SendMessage enforces. Nothing calls them; the named routes below are the whole surface.
        [NonAction]
        public override Task Delete(Guid id)
            => throw new NotSupportedException("Chat messages are removed by the retention runner.");

        [NonAction]
        public override Task<MatchChatDto> GetById(Guid id)
            => throw new NotSupportedException("Use GET api/matchchat/{matchId}/history.");

        [NonAction]
        public override Task<EntityListDto<MatchChatDto>> GetList(
            int? pageIndex,
            int? pageSize,
            List<SortItem> sortItems,
            List<FilterItem> filterItems)
            => throw new NotSupportedException("Use GET api/matchchat/{matchId}/history.");

        [NonAction]
        public override Task<MatchChatDto> SaveEntity(MatchChatPost modelSave)
            => throw new NotSupportedException("Use POST api/matchchat/{matchId}.");

        [HttpGet("{matchId}/history")]
        public async Task<IActionResult> GetHistory(Guid matchId, [FromQuery] int? take = null, [FromQuery] DateTime? before = null)
        {
            var history = await chatService.GetHistory(matchId, take, before);
            return Ok(history);
        }

        [HttpPost("{matchId}")]
        public async Task<IActionResult> SendMessage(Guid matchId, [FromBody] CreateMessageDto body)
        {
            var result = await chatService.SendMessage(matchId, body.Content);
            return Ok(result);
        }

        [HttpGet("{matchId}/mute")]
        public async Task<IActionResult> GetMuted(Guid matchId)
        {
            return Ok(new { muted = await chatService.GetMuted(matchId) });
        }

        /// <summary>
        /// Mute / unmute this match's chat for the caller — no push, no Discord DM, no badge, while
        /// the thread stays in the inbox with its real unread count.
        /// </summary>
        [HttpPut("{matchId}/mute")]
        public async Task<IActionResult> SetMuted(Guid matchId, [FromBody] SetMatchChatMutedRequest body)
        {
            await chatService.SetMuted(matchId, body.Muted);
            return NoContent();
        }

        [HttpPost("{matchId}/read")]
        public async Task<IActionResult> MarkRead(Guid matchId)
        {
            await chatService.MarkRead(matchId);
            return NoContent();
        }
    }
}
