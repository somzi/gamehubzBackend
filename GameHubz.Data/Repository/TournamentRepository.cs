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
    public class TournamentRepository : BaseRepository<ApplicationContext, TournamentEntity>, ITournamentRepository
    {
        public TournamentRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<TournamentEntity?> GetWithParticipents(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentParticipants)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<List<TournamentOverview>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize)
        {
            List<TournamentStatus> statuses = [];

            if (status == TournamentStatus.Draft || status == TournamentStatus.RegistrationOpen || status == TournamentStatus.RegistrationClosed)
            {
                statuses.Add(TournamentStatus.Draft);
                statuses.Add(TournamentStatus.RegistrationClosed);
                statuses.Add(TournamentStatus.RegistrationOpen);
            }
            else
            {
                statuses.Add(status);
            }

            var query = this.BaseDbSet()
                .Where(x => x.HubId == hubId && statuses.Contains(x.Status));

            var items = await query
                .OrderByDescending(x => x.StartDate)
                .Skip(page * pageSize)
                .Take(pageSize)
                 .Select(x => new TournamentOverview
                 {
                     Name = x.Name,
                     Region = x.Region,
                     StartDate = x.StartDate ?? DateTime.MinValue,
                     NumberOfParticipants = x.TournamentParticipants!.Count(),
                     Prize = x.Prize,
                     PrizeCurrency = x.PrizeCurrency,
                     Status = x.Status,
                     Id = x.Id!.Value!
                 })
                .ToListAsync();

            return items;
        }

        public async Task<List<TournamentOverview>> GetByHubsPaged(
            Guid userId,
            List<Guid> hubIds,
            TournamentUserStatus filter,
            int page,
            int pageSize)
        {
            var query = ApplyFilters(userId, hubIds, filter);

            return await query
                .OrderByDescending(x => x.StartDate)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(x => new TournamentOverview
                {
                    Name = x.Name,
                    Region = x.Region,
                    StartDate = x.StartDate ?? DateTime.MinValue,
                    NumberOfParticipants = x.TournamentParticipants!.Count(),
                    Prize = x.Prize,
                    Status = x.Status,
                    PrizeCurrency = x.PrizeCurrency,
                    Id = x.Id!.Value
                })
                .ToListAsync();
        }

        public async Task<int> GetCountByHubs(
            Guid userId,
            List<Guid> hubIds,
            TournamentUserStatus filter)
        {
            var query = ApplyFilters(userId, hubIds, filter);
            return await query.CountAsync();
        }

        public async Task<int> GetByHubCount(Guid hubId, TournamentStatus status)
        {
            var query = this.BaseDbSet()
                .Where(x => x.HubId == hubId && x.Status == status);

            return await query.CountAsync();
        }

        public async Task<TournamentEntity> GetWithPendingRegistration(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentRegistrations!
                    .Where(tr => tr.Status == TournamentRegistrationStatus.Pending))
                .SingleAsync(x => x.Id == id);
        }

        public async Task<TournamentEntity?> GetWithFullDetails(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .AsNoTracking() // Read-only speed boost
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.TournamentGroups)
                // Load Matches for every stage
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.Matches!)
                        .ThenInclude(m => m.HomeParticipant)
                            .ThenInclude(p => p!.User) // Get Username/Avatar
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.Matches!)
                        .ThenInclude(m => m.AwayParticipant)
                            .ThenInclude(p => p!.User)
                .FirstOrDefaultAsync(t => t.Id == tournamentId);
        }

        public async Task<TournamentOverview?> GetOverview(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == tournamentId)
                  .Select(x => new TournamentOverview
                  {
                      Name = x.Name,
                      Region = x.Region,
                      StartDate = x.StartDate ?? DateTime.MinValue,
                      NumberOfParticipants = x.TournamentParticipants!.Count(),
                      Prize = x.Prize,
                      Status = x.Status,
                      PrizeCurrency = x.PrizeCurrency,
                      Id = x.Id!.Value,
                      MaxPlayers = x.MaxPlayers!.Value,
                      Description = x.Description ?? string.Empty,
                      Rules = x.Rules ?? string.Empty,
                      CreatedBy = x.CreatedBy!.Value
                  }).FirstOrDefaultAsync();
        }

        private IQueryable<TournamentEntity> ApplyFilters(
            Guid userId,
            List<Guid> hubIds,
            TournamentUserStatus filter)
        {
            var query = this.BaseDbSet()
                .AsNoTracking()
                .Where(x => hubIds.Contains(x.HubId!.Value));

            switch (filter)
            {
                case TournamentUserStatus.AvailableToJoin:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.RegistrationOpen &&
                        !x.TournamentParticipants!.Any(tp => tp.UserId == userId) &&
                        (x.RegistrationDeadline == null || x.RegistrationDeadline > DateTime.UtcNow)
                    );
                    break;

                case TournamentUserStatus.Upcoming:
                    query = query.Where(x =>
                        (x.Status == TournamentStatus.RegistrationOpen ||
                         x.Status == TournamentStatus.RegistrationClosed) &&
                        x.TournamentParticipants!.Any(tp => tp.UserId == userId)
                    );
                    break;

                case TournamentUserStatus.Live:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.InProgress &&
                        x.TournamentParticipants!.Any(tp => tp.UserId == userId)
                    );
                    break;

                case TournamentUserStatus.Completed:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.Completed &&
                        x.TournamentParticipants!.Any(tp => tp.UserId == userId)
                    );
                    break;

                default:
                    throw new ArgumentException($"Invalid filter: {filter}");
            }

            return query;
        }
    }
}