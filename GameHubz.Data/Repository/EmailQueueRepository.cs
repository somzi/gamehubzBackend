using Microsoft.EntityFrameworkCore;
using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data.Repository
{
    public class EmailQueueRepository : BaseRepository<ApplicationContext, EmailQueueEntity>, IEmailQueueRepository
    {
        public EmailQueueRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<EmailQueueEntity?> GetNextEmailQueue()
        {
            return await this.BaseDbSet()
                .Where(x => x.Status == EmailQueueStatus.Pending)
                .OrderBy(x => x.CreatedOn)
                .FirstOrDefaultAsync();
        }
    }
}
