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

        public async Task<List<TournamentParticipantEntity>> GetByGroupId(Guid? id)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentGroupId == id)
                .ToListAsync();
        }

        public async Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId)
                .Select(x => new TournamentParticipantOverview
                {
                    Username = x.User!.Username,
                    UserId = x.User!.Id!.Value
                })
                .ToListAsync();
        }

        public async Task<List<TournamentOverview>> GetByUserId(Guid userid)
        {
            return await this.BaseDbSet()
                .Where(x => x.UserId == userid)
                .Select(x => new TournamentOverview
                {
                    Id = x.Tournament!.Id!.Value,
                    MaxPlayers = x.Tournament.MaxPlayers ?? 0,
                    Name = x.Tournament.Name,
                    NumberOfParticipants = x.Tournament.TournamentParticipants!.Count(),
                    Prize = x.Tournament.Prize,
                    PrizeCurrency = x.Tournament.PrizeCurrency,
                    Region = x.Tournament.Region,
                    StartDate = x.Tournament.StartDate!.Value
                })
                .ToListAsync();
        }
    }
}