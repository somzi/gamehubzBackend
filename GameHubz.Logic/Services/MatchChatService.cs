using FluentValidation;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    public class MatchChatService : AppBaseServiceGeneric<MatchChatEntity, MatchChatDto, MatchChatPost, MatchChatEdit>
    {
        private readonly IHubContext<MatchChatHub> hubContext;

        public MatchChatService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchChatEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            IHubContext<MatchChatHub> hubContext) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.hubContext = hubContext;
        }

        public async Task<ChatMessageDto> SendMessage(Guid matchId, string content)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var entity = new MatchChatEntity
            {
                MatchId = matchId,
                UserId = user.UserId,
                Content = content,
            };

            await this.AppUnitOfWork.MatchChatRepository.AddEntity(entity, this.UserContextReader);
            await this.SaveAsync();

            var dto = new ChatMessageDto
            {
                Id = entity.Id!.Value,
                UserId = user.UserId,
                UserNickname = user.Username,
                Content = content,
                SentAt = entity.CreatedOn!.Value
            };

            await hubContext.Clients.Group(matchId.ToString())
                             .SendAsync("ReceiveMessage", dto);

            return dto;
        }

        public async Task<List<ChatMessageDto>> GetHistory(Guid matchId)
        {
            return await this.AppUnitOfWork.MatchChatRepository.GetByMatchId(matchId);
        }

        protected override IRepository<MatchChatEntity> GetRepository()
            => this.AppUnitOfWork.MatchChatRepository;
    }
}