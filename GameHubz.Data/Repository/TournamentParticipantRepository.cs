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
                    UserId = x.User!.Id!.Value,
                    AvatarUrl = x.User.AvatarUrl
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
                    Status = x.Tournament.Status,
                    Region = x.Tournament.Region,
                    StartDate = x.Tournament.StartDate!.Value,
                    IsTeamTournament = x.Tournament.IsTeamTournament
                })
                .ToListAsync();
        }

        public async Task<EntityListDto<TournamentOverview>> GetByUserIdPaged(Guid userid, int pageNumber, int pageSize)
        {
            var query = this.BaseDbSet()
                .Where(x => x.UserId == userid);

            var count = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.Tournament!.StartDate)
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .Select(x => new TournamentOverview
                {
                    Id = x.Tournament!.Id!.Value,
                    MaxPlayers = x.Tournament.MaxPlayers ?? 0,
                    Name = x.Tournament.Name,
                    NumberOfParticipants = x.Tournament.TournamentParticipants!.Count(),
                    Prize = x.Tournament.Prize,
                    PrizeCurrency = x.Tournament.PrizeCurrency,
                    Status = x.Tournament.Status,
                    Region = x.Tournament.Region,
                    StartDate = x.Tournament.StartDate!.Value,
                    HubAvatarUrl = x.Tournament.Hub!.AvatarUrl,
                    HubName = x.Tournament.Hub.Name,
                    Format = x.Tournament.Format,
                    RoundDurationMinutes = x.Tournament.RoundDurationMinutes,
                    IsTeamTournament = x.Tournament.IsTeamTournament
                })
                .ToListAsync();

            return new EntityListDto<TournamentOverview>(items, count);
        }

        public Task<TournamentParticipantEntity> GetUserByTournamentId(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .FirstAsync(tp => tp.TournamentId == tournamentId && tp.UserId == userId);
        }

        public Task<TournamentParticipantEntity?> GetByTeamId(Guid teamId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(tp => tp.TeamId == teamId);
        }
    }
}