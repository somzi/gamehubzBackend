using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TournamentParticipantRepository : BaseRepository<ApplicationContext, TournamentParticipantEntity>, ITournamentParticipantRepository
    {
        public TournamentParticipantRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId)
                .Select(x => new TournamentParticipantOverview
                {
                    Username = x.User!.Username
                })
                .ToListAsync();
        }
    }
}