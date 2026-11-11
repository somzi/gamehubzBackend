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
    public class TournamentInviteRepository : BaseRepository<ApplicationContext, TournamentInviteEntity>, ITournamentInviteRepository
    {
        public TournamentInviteRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<TournamentInviteEntity?> GetPending(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId && x.UserId == userId && x.Status == TournamentInviteStatus.Pending)
                .FirstOrDefaultAsync();
        }

        public Task<TournamentInviteEntity?> GetLatest(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId && x.UserId == userId)
                .OrderByDescending(x => x.CreatedOn)
                .FirstOrDefaultAsync();
        }

        public Task<bool> HasDeclined(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .AnyAsync(x => x.TournamentId == tournamentId && x.UserId == userId && x.Status == TournamentInviteStatus.Declined);
        }

        public async Task<Dictionary<Guid, TournamentInviteStatus>> GetInviteStates(Guid tournamentId, List<Guid> userIds)
        {
            if (userIds.Count == 0)
            {
                return new Dictionary<Guid, TournamentInviteStatus>();
            }

            var rows = await this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId
                    && userIds.Contains(x.UserId)
                    && (x.Status == TournamentInviteStatus.Pending || x.Status == TournamentInviteStatus.Declined))
                .Select(x => new { x.UserId, x.Status })
                .ToListAsync();

            // Pending wins over an older decline: the player has an open invite right now.
            return rows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Any(x => x.Status == TournamentInviteStatus.Pending)
                        ? TournamentInviteStatus.Pending
                        : TournamentInviteStatus.Declined);
        }

        public async Task<List<TournamentInviteDto>> GetForTournament(Guid tournamentId)
        {
            var users = this.ContextBase.Set<UserEntity>();

            return await this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId)
                .OrderByDescending(x => x.CreatedOn)
                .Select(x => new TournamentInviteDto
                {
                    Id = x.Id!.Value,
                    TournamentId = x.TournamentId,
                    UserId = x.UserId,
                    Username = x.User != null ? x.User.Username : string.Empty,
                    AvatarUrl = x.User != null ? x.User.AvatarUrl : null,
                    Kind = x.Kind,
                    Status = x.Status,
                    IsGuest = x.IsGuest,
                    SwapOutgoingUserId = x.SwapOutgoingUserId,
                    SwapOutgoingUsername = x.SwapOutgoingUserId == null
                        ? null
                        : users.Where(u => u.Id == x.SwapOutgoingUserId).Select(u => u.Username).FirstOrDefault(),
                    CreatedOn = x.CreatedOn!.Value,
                    RespondedOn = x.RespondedOn,
                })
                .ToListAsync();
        }

        public async Task<bool> TryTransition(Guid inviteId, TournamentInviteStatus from, TournamentInviteStatus to, DateTime now, Guid? modifiedBy)
        {
            int updated = await this.BaseDbSet()
                .Where(x => x.Id == inviteId && x.Status == from)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, to)
                    .SetProperty(x => x.RespondedOn, now)
                    .SetProperty(x => x.ModifiedOn, now)
                    .SetProperty(x => x.ModifiedBy, modifiedBy));

            return updated == 1;
        }

        public Task<int> ExpireOpenSwapInvites(Guid tournamentId, Guid outgoingUserId, Guid exceptInviteId, DateTime now)
        {
            return this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId
                    && x.Kind == TournamentInviteKind.Swap
                    && x.SwapOutgoingUserId == outgoingUserId
                    && x.Status == TournamentInviteStatus.Pending
                    && x.Id != exceptInviteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, TournamentInviteStatus.Expired)
                    .SetProperty(x => x.RespondedOn, now)
                    .SetProperty(x => x.ModifiedOn, now));
        }

        public async Task<HashSet<Guid>> GetUserIdsWithExclusiveAccess(Guid hubId, List<Guid> userIds)
        {
            if (userIds.Count == 0)
            {
                return new HashSet<Guid>();
            }

            var ids = await this.ContextBase.Set<UserHubEntity>()
                .AsNoTracking()
                .Where(m => m.HubId == hubId
                    && m.UserId != null
                    && userIds.Contains(m.UserId.Value)
                    && (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin || m.HubRole == HubRole.HubExclusive))
                .Select(m => m.UserId!.Value)
                .ToListAsync();

            return ids.ToHashSet();
        }

        public async Task<List<UserEntity>> GetCandidatesPaged(
            Guid tournamentId,
            Guid hubId,
            Guid callerUserId,
            string? search,
            bool onlyEligible,
            List<string>? countries,
            RegionType region,
            bool exclusive,
            int pageNumber,
            int pageSize)
        {
            var members = this.ContextBase.Set<UserHubEntity>();
            var participants = this.ContextBase.Set<TournamentParticipantEntity>();
            var bans = this.ContextBase.Set<UserHubBanEntity>();

            var query = this.ContextBase.Set<UserEntity>()
                .AsNoTracking()
                .Where(u => u.IsActive && u.Id != callerUserId);

            if (string.IsNullOrWhiteSpace(search))
            {
                // Nothing typed yet: the hub's own members are the natural first list.
                query = query.Where(u => members.Any(m => m.HubId == hubId && m.UserId == u.Id));
            }
            else
            {
                var term = search.Trim().ToLower();
                query = query.Where(u => u.Username.ToLower().Contains(term));
            }

            // Everything below is in SQL for the same reason as the swap picker: filtering a page
            // after it was cut would leave holes in it.
            query = query.Where(u => !participants.Any(p => p.TournamentId == tournamentId && p.UserId == u.Id));
            query = query.Where(u => !bans.Any(b => b.HubId == hubId && b.UserId == u.Id));

            if (onlyEligible)
            {
                // Mirrors TournamentRegistrationService.IsEligibleToJoin and the exclusive rule.
                if (countries != null && countries.Count > 0)
                {
                    query = query.Where(u => u.Country != null && countries.Contains(u.Country));
                }
                else if (region != RegionType.GLOBAL)
                {
                    query = query.Where(u => u.Region == region);
                }

                if (exclusive)
                {
                    query = query.Where(u => members.Any(m => m.HubId == hubId && m.UserId == u.Id
                        && (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin || m.HubRole == HubRole.HubExclusive)));
                }
            }

            return await query
                .OrderBy(u => u.Username)
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}
