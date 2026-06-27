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

        public async Task<List<TournamentParticipantEntity>> GetForLeagueResync(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId && tp.TournamentGroupId == null)
                .ToListAsync();
        }

        public async Task<List<TournamentParticipantEntity>> GetEntitiesByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId)
                .ToListAsync();
        }

        public async Task<List<TournamentParticipantEntity>> GetByGroupIdWithNames(Guid? id)
        {
            return await this.BaseDbSet()
                .Include(tp => tp.User)
                .Include(tp => tp.Team)
                .Where(tp => tp.TournamentGroupId == id)
                .ToListAsync();
        }

        public async Task<List<TournamentParticipantEntity>> GetByGroupIdsWithNames(List<Guid> groupIds)
        {
            return await this.BaseDbSet()
                .Include(tp => tp.User)
                .Include(tp => tp.Team)
                .Where(tp => tp.TournamentGroupId != null && groupIds.Contains(tp.TournamentGroupId.Value))
                .ToListAsync();
        }

        public async Task<List<TournamentParticipantOverview>?> GetByTournamentId(Guid tournamentId)
        {
            // Skip rows without a User: team participants (UserId == null) and any stale
            // rows where the user was hard-deleted leave User null, and projecting
            // x.User!.Id!.Value on those threw "Nullable object must have a value".
            var rows = await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId
                    && tp.User != null
                    && tp.User.Id != null)
                .Select(x => new TournamentParticipantOverview
                {
                    Username = x.User!.Username,
                    UserId = x.User!.Id!.Value,
                    AvatarUrl = x.User.AvatarUrl
                })
                .ToListAsync();

            // Collapse duplicate participant rows for the same user into a single entry.
            // A user could historically end up with more than one participant row (e.g. the
            // same registration approved repeatedly), which returned the same UserId twice and
            // crashed the client's list rendering on duplicate keys.
            return rows
                .GroupBy(r => r.UserId)
                .Select(g => g.First())
                .ToList();
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
                .Where(x => x.UserId == userid
                    || (x.TeamId != null && x.Team!.Members.Any(m => m.UserId == userid)));

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

        // Returns every participant row for the user — used by removal so duplicate rows are
        // all cleaned up in one go and an empty result simply removes nothing (no throw).
        public async Task<List<TournamentParticipantEntity>> GetAllByTournamentAndUser(Guid tournamentId, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId && tp.UserId == userId)
                .ToListAsync();
        }

        public Task<bool> ExistsForUser(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .AnyAsync(tp => tp.TournamentId == tournamentId && tp.UserId == userId);
        }

        public Task<bool> ExistsForTeam(Guid tournamentId, Guid teamId)
        {
            return this.BaseDbSet()
                .AnyAsync(tp => tp.TournamentId == tournamentId && tp.TeamId == teamId);
        }

        public Task<TournamentParticipantEntity?> GetByTeamId(Guid teamId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(tp => tp.TeamId == teamId);
        }

        public async Task<List<Guid>> GetAllUserIdsByTournamentId(Guid tournamentId)
        {
            var soloUserIds = this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId && tp.UserId.HasValue)
                .Select(tp => tp.UserId!.Value);

            var teamMemberUserIds = this.BaseDbSet()
                .Where(tp => tp.TournamentId == tournamentId && tp.TeamId.HasValue)
                .SelectMany(tp => tp.Team!.Members)
                .Where(m => m.UserId.HasValue)
                .Select(m => m.UserId!.Value);

            return await soloUserIds
                .Union(teamMemberUserIds)
                .Distinct()
                .ToListAsync();
        }
    }
}