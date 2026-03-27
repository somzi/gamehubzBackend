using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchChatRepository : BaseRepository<ApplicationContext, MatchChatEntity>, IMatchChatRepository
    {
        public MatchChatRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<ChatMessageDto>> GetByMatchId(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId)
                .Select(x => new ChatMessageDto
                {
                    Id = x.Id!.Value,
                    UserId = x.UserId!.Value,
                    UserNickname = x.User!.Nickname ?? x.User!.Username,
                    Content = x.Content,
                    SentAt = x.CreatedOn!.Value
                })
                .OrderBy(x => x.SentAt)
                .ToListAsync();
        }
    }
}