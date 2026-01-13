using Microsoft.EntityFrameworkCore;
using Template.Data.Context;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data.UnitOfWork
{
    public class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IAppUnitOfWork scopedPutDomUnitOfWork;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public UnitOfWorkFactory(
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            DbContextOptions<ApplicationContext> applicationContextOptions,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
        {
            this.scopedPutDomUnitOfWork = new AppUnitOfWork(
                new ApplicationContext(applicationContextOptions),
                dateTimeProvider,
                filterExpressionBuilder,
                sortStringBuilder,
                localizationService);
        }

        public IAppUnitOfWork CreateAppUnitOfWork()
        {
            return this.scopedPutDomUnitOfWork;
        }
    }
}