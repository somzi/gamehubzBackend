using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

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

        public async Task<List<TournamentRegistrationEntity>> GetByIds(List<Guid> ids)
        {
            return await this.BaseDbSet()
                .Where(x => ids.Contains(x.Id!.Value))
                .ToListAsync();
        }

        public async Task<List<TournamentRegistrationOverview>> GetPendingByTournamenId(Guid tournamentId)
        {
            return await this.BaseDbSet()
               .Where(tp => tp.TournamentId == tournamentId && tp.Status == TournamentRegistrationStatus.Pending)
               .Select(x => new TournamentRegistrationOverview
               {
                   Username = x.User!.Username
               })
               .ToListAsync();
        }
    }
}