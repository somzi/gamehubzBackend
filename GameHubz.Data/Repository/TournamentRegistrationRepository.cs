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
                .Include(x => x.Tournament)
                    .ThenInclude(x => x!.TournamentParticipants)
                .Where(x => ids.Contains(x.Id!.Value))
                .ToListAsync();
        }

        // Pending registrations awaiting approval across every tournament owned by the
        // given hubs — organizer badge. Indexed on TournamentRegistration.Status.
        // Only tournaments still accepting registrations (Draft / RegistrationOpen /
        // RegistrationClosed) count — a stale pending row on a live/finished/cancelled tournament
        // isn't actionable and would otherwise inflate the badge above what any list can show.
        public async Task<int> CountPendingForHubs(List<Guid> hubIds)
        {
            if (hubIds == null || hubIds.Count == 0) return 0;

            return await this.BaseDbSet()
                .CountAsync(r => r.Status == TournamentRegistrationStatus.Pending
                    && r.Tournament!.HubId != null
                    && hubIds.Contains(r.Tournament.HubId.Value)
                    && (r.Tournament.Status == TournamentStatus.Draft
                        || r.Tournament.Status == TournamentStatus.RegistrationOpen
                        || r.Tournament.Status == TournamentStatus.RegistrationClosed));
        }

        // Per-tournament pending registration counts across the given hubs — drives the
        // cascade badge on each tournament's Requests/Registrations tab.
        public async Task<List<TournamentCountRow>> GetPendingCountsByTournament(List<Guid> hubIds)
        {
            if (hubIds == null || hubIds.Count == 0) return new List<TournamentCountRow>();

            return await this.BaseDbSet()
                .Where(r => r.Status == TournamentRegistrationStatus.Pending
                    && r.TournamentId != null
                    && r.Tournament!.HubId != null
                    && hubIds.Contains(r.Tournament.HubId.Value)
                    && (r.Tournament.Status == TournamentStatus.Draft
                        || r.Tournament.Status == TournamentStatus.RegistrationOpen
                        || r.Tournament.Status == TournamentStatus.RegistrationClosed))
                .GroupBy(r => new { TournamentId = r.TournamentId!.Value, HubId = r.Tournament!.HubId!.Value, Status = (int)r.Tournament!.Status })
                .Select(g => new TournamentCountRow
                {
                    TournamentId = g.Key.TournamentId,
                    HubId = g.Key.HubId,
                    Status = g.Key.Status,
                    Count = g.Count()
                })
                .ToListAsync();
        }

        public async Task<List<TournamentRegistrationOverview>> GetPendingByTournamenId(Guid tournamentId)
        {
            return await this.BaseDbSet()
               .Where(tp => tp.TournamentId == tournamentId && tp.Status == TournamentRegistrationStatus.Pending)
               .Select(x => new TournamentRegistrationOverview
               {
                   Id = x.Id!.Value,
                   UserId = (x.TeamId.HasValue ? x.Team!.CaptainUserId : x.UserId) ?? Guid.Empty,
                   Username = x.TeamId.HasValue
                       ? x.Team!.CaptainUser!.Username
                       : x.User!.Username,
                   AvatarUrl = x.TeamId.HasValue
                       ? x.Team!.CaptainUser!.AvatarUrl
                       : x.User!.AvatarUrl,
                   IsTeamRegistration = x.TeamId.HasValue,
                   TeamId = x.TeamId,
                   TeamName = x.TeamId.HasValue ? x.Team!.TeamName : null,
                   CaptainUserId = x.TeamId.HasValue ? x.Team!.CaptainUserId : null,
                   MemberCount = x.TeamId.HasValue ? x.Team!.Members.Count() : null,
                   TeamSize = x.TeamId.HasValue ? x.Team!.Tournament!.TeamSize : null,
                   Members = x.TeamId.HasValue
                       ? x.Team!.Members.Select(m => new TeamMemberDto
                       {
                           UserId = m.UserId!.Value,
                           Username = m.User!.Username,
                           AvatarUrl = m.User.AvatarUrl
                       }).ToList()
                       : new List<TeamMemberDto>()
               })
               .ToListAsync();
        }

        public Task<TournamentRegistrationEntity> GetUserByTournamentId(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .FirstAsync(tp => tp.TournamentId == tournamentId && tp.UserId == userId);
        }

        // Returns every registration row for the user — used by removal so duplicate rows are
        // all cleaned up in one go and an empty result simply removes nothing (no throw).
        public async Task<List<TournamentRegistrationEntity>> GetAllByTournamentAndUser(Guid tournamentId, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(r => r.TournamentId == tournamentId && r.UserId == userId)
                .ToListAsync();
        }

        // True if the user or team already has a registration that isn't rejected — used to
        // block duplicate sign-ups at registration time. A rejected row doesn't count, so a
        // rejected entrant can register again.
        public Task<bool> ExistsNonRejected(Guid tournamentId, Guid? userId, Guid? teamId)
        {
            return this.BaseDbSet().AnyAsync(r =>
                r.TournamentId == tournamentId
                && r.Status != TournamentRegistrationStatus.Rejected
                && ((userId != null && r.UserId == userId) || (teamId != null && r.TeamId == teamId)));
        }

        public Task<TournamentRegistrationEntity> GetWithTournament(Guid registrationId)
        {
            return this.BaseDbSet()
                .Include(x => x.Tournament)
                    .ThenInclude(x => x!.TournamentParticipants)
                .FirstAsync(x => x.Id == registrationId);
        }

        public Task<TournamentRegistrationEntity?> GetByTeamId(Guid teamId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(x => x.TeamId == teamId);
        }
    }
}