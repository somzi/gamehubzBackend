using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TournamentStageRepository : BaseRepository<ApplicationContext, TournamentStageEntity>, ITournamentStageRepository
    {
        public TournamentStageRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<TournamentStageEntity> GetByOrder(Guid tournamentId, int order)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(ts => ts.TournamentId == tournamentId && ts.Order == order)!;
        }

        public Task<TournamentStageEntity> GetByTournamentId(Guid tournamentId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(ts => ts.TournamentId == tournamentId && ts.Type == StageType.SingleEliminationBracket)!;
        }

        public async Task<TournamentStageEntity?> GetWithGroupsAndMatches(Guid Id)
        {
            return await this.BaseDbSet()
                 .Include(ts => ts.TournamentGroups!)
                 .Include(ts => ts.Matches!)
                 .FirstOrDefaultAsync();
        }
    }
}