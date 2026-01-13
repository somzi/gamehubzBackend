using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data.Repository
{
    public class TournamentRegistrationRepository : BaseRepository<ApplicationContext, TournamentRegistrationEntity>, ITournamentRegistrationRepository
    {
        public TournamentRegistrationRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }
    }
}