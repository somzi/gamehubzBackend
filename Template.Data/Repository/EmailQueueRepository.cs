using Microsoft.EntityFrameworkCore;
using Template.Data.Base;
using Template.Data.Context;
using Template.DataModels.Domain;
using Template.DataModels.Enums;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data.Repository
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