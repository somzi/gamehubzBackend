using GameHubz.Data.Context;
using GameHubz.Data.UnitOfWork;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// Wraps a pre-built <see cref="ApplicationContext"/> as an <see cref="IUnitOfWorkFactory"/>. Like the
    /// production factory it hands the same UnitOfWork to every collaborator built from it (so the
    /// services in one logical operation share a change-tracker), but it lets the test choose the
    /// context instance — an in-memory or SQLite-backed one, or the Countries-patched test subclass.
    /// </summary>
    internal sealed class TestUnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IAppUnitOfWork unitOfWork;

        public TestUnitOfWorkFactory(ApplicationContext context, ILocalizationService localizationService)
        {
            this.unitOfWork = new AppUnitOfWork(
                context,
                new DateTimeProvider(),
                new FilterExpressionBuilder(),
                new SortStringBuilder(),
                localizationService);
        }

        public IAppUnitOfWork CreateAppUnitOfWork() => this.unitOfWork;
    }
}
