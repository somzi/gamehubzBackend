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

        // Atomic CAS: exactly one request can move a tournament into InProgress and generate
        // its bracket — a concurrent or repeated CreateBracket sees 0 rows updated and bails.
        public async Task<bool> TryClaimBracketGeneration(Guid tournamentId)
        {
            int affected = await this.BaseDbSet()
                .Where(t => t.Id == tournamentId
                    && t.Status != TournamentStatus.InProgress
                    && t.Status != TournamentStatus.Completed)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TournamentStatus.InProgress));

            return affected > 0;
        }

        // Gives the claim back after a failed generation so the organizer can fix the config
        // (e.g. invalid qualifier count) and retry, instead of the tournament wedging in
        // InProgress with no bracket. Conditional on InProgress so a finished tournament is
        // never downgraded.
        public async Task RestoreBracketGenerationClaim(Guid tournamentId, TournamentStatus previousStatus)
        {
            await this.BaseDbSet()
                .Where(t => t.Id == tournamentId && t.Status == TournamentStatus.InProgress)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, previousStatus));
        }

        /// <summary>
        /// Serialises stage-advancement logic per tournament via a Postgres session advisory
        /// lock. Round-completion checks and next-round / knockout generation are check-then-act
        /// over multiple rows, so without this two concurrent "last results" of a round can both
        /// generate the next round. The connection is pinned open so every SaveChanges between
        /// acquire and release runs on the same session that owns the lock; Npgsql's pool reset
        /// (DISCARD ALL) releases the lock even if the request dies before the explicit unlock.
        /// </summary>
        public async Task AcquireAdvancementLock(Guid tournamentId)
        {
            await this.ContextBase.Database.OpenConnectionAsync();
            await this.ContextBase.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock({0})", ToAdvisoryKey(tournamentId));
        }

        public async Task ReleaseAdvancementLock(Guid tournamentId)
        {
            try
            {
                await this.ContextBase.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_unlock({0})", ToAdvisoryKey(tournamentId));
            }
            finally
            {
                // Balances the OpenConnection in Acquire — EF reopens on demand afterwards.
                await this.ContextBase.Database.CloseConnectionAsync();
            }
        }

        // Folds the Guid into the bigint key space pg_advisory_lock expects. A collision between
        // two tournaments only over-serialises them — never under-locks.
        private static long ToAdvisoryKey(Guid id)
        {
            var bytes = id.ToByteArray();
            return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
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
                     Countries = x.Countries,
                     StartDate = x.StartDate ?? DateTime.MinValue,
                     NumberOfParticipants = x.TournamentParticipants!.Count(),
                     Prize = x.Prize,
                     PrizeCurrency = x.PrizeCurrency,
                     Status = x.Status,
                     Id = x.Id!.Value!,
                     IsTeamTournament = x.IsTeamTournament
                 })
                .ToListAsync();

            return items;
        }

        public async Task<List<TournamentOverview>> GetByHubsPaged(
            Guid userId,
            List<Guid> hubIds,
            TournamentUserStatus filter,
            RegionType region,
            string? userCountry,
            int page,
            int pageSize)
        {
            var query = ApplyFilters(userId, hubIds, region, userCountry, filter);

            return await query
                .OrderByDescending(x => x.StartDate)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(x => new TournamentOverview
                {
                    Name = x.Name,
                    Region = x.Region,
                    Countries = x.Countries,
                    StartDate = x.StartDate ?? DateTime.MinValue,
                    NumberOfParticipants = x.TournamentParticipants!.Count(),
                    Prize = x.Prize,
                    Status = x.Status,
                    PrizeCurrency = x.PrizeCurrency,
                    Id = x.Id!.Value,
                    HubName = x.Hub!.Name,
                    HubAvatarUrl = x.Hub.AvatarUrl,
                    Format = x.Format,
                    RoundDurationMinutes = x.RoundDurationMinutes,
                    IsTeamTournament = x.IsTeamTournament,
                    TeamWinCondition = x.TeamWinCondition
                })
                .ToListAsync();
        }

        public async Task<int> GetCountByHubs(
            Guid userId,
            List<Guid> hubIds,
            RegionType region,
            string? userCountry,
            TournamentUserStatus filter)
        {
            var query = ApplyFilters(userId, hubIds, region, userCountry, filter);
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
                .Include(x => x.TournamentParticipants)
                .Include(x => x.TournamentRegistrations!
                    .Where(tr => tr.Status == TournamentRegistrationStatus.Pending))
                .SingleAsync(x => x.Id == id);
        }

        public async Task<TournamentEntity?> GetWithFullDetails(Guid tournamentId)
        {
            // Lightweight PK lookup to determine tournament type before building the heavy query
            var isTeam = await this.BaseDbSet()
                .Where(t => t.Id == tournamentId)
                .Select(t => (bool?)t.IsTeamTournament)
                .FirstOrDefaultAsync();

            if (isTeam == null) return null;

            // AsSplitQuery prevents cartesian explosion — each Include path
            // becomes a separate SQL query instead of one massive LEFT JOIN product
            IQueryable<TournamentEntity> query = this.BaseDbSet()
                .AsNoTracking()
                .AsSplitQuery()
                .Include(t => t.Hub)
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.TournamentGroups)
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.Matches!)
                        .ThenInclude(m => m.HomeParticipant)
                            .ThenInclude(p => p!.User)
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.Matches!)
                        .ThenInclude(m => m.AwayParticipant)
                            .ThenInclude(p => p!.User)
                .Include(t => t.TournamentStages!)
                    .ThenInclude(s => s.Matches!)
                        .ThenInclude(m => m.MatchEvidences);

            if (isTeam.Value)
            {
                query = query
                    .Include(t => t.TournamentStages!)
                        .ThenInclude(s => s.TeamMatches!)
                            .ThenInclude(tm => tm.SubMatches)
                    .Include(t => t.TournamentStages!)
                        .ThenInclude(s => s.TeamMatches!)
                            .ThenInclude(tm => tm.HomeTeamParticipant)
                                .ThenInclude(p => p!.Team)
                    .Include(t => t.TournamentStages!)
                        .ThenInclude(s => s.TeamMatches!)
                            .ThenInclude(tm => tm.AwayTeamParticipant)
                                .ThenInclude(p => p!.Team);
            }

            return await query.FirstOrDefaultAsync(t => t.Id == tournamentId);
        }

        public async Task<TournamentOverview?> GetOverview(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == tournamentId)
                  .Select(x => new TournamentOverview
                  {
                      Name = x.Name,
                      Region = x.Region,
                      Countries = x.Countries,
                      StartDate = x.StartDate ?? DateTime.MinValue,
                      NumberOfParticipants = x.TournamentParticipants!.Count(),
                      Prize = x.Prize,
                      Status = x.Status,
                      PrizeCurrency = x.PrizeCurrency,
                      Id = x.Id!.Value,
                      MaxPlayers = x.MaxPlayers!.Value,
                      Description = x.Description ?? string.Empty,
                      Rules = x.Rules ?? string.Empty,
                      CreatedBy = x.CreatedBy!.Value,
                      RegistrationDeadLine = x.RegistrationDeadline,
                      HubId = x.HubId!.Value,
                      Format = x.Format,
                      RoundDurationMinutes = x.RoundDurationMinutes,
                      SwissRoundsCount = x.SwissRoundsCount,
                      SwissKnockoutQualifiers = x.SwissKnockoutQualifiers,
                      SwissDirectQualifiers = x.SwissDirectQualifiers,
                      HubName = x.Hub!.Name,
                      IsTeamTournament = x.IsTeamTournament,
                      TeamSize = x.TeamSize,
                      TeamWinCondition = x.TeamWinCondition,
                      HasThirdPlaceMatch = x.HasThirdPlaceMatch,
                      RequireResultApproval = x.RequireResultApproval
                  }).FirstOrDefaultAsync();
        }

        private IQueryable<TournamentEntity> ApplyFilters(
            Guid userId,
            List<Guid> hubIds,
            RegionType region,
            string? userCountry,
            TournamentUserStatus filter)
        {
            // Visibility:
            //  - Region-scoped tournaments (Countries == null): match the user's region or GLOBAL.
            //  - Country-scoped tournaments (Countries set): visible only to users whose country is
            //    in the list (Npgsql translates Contains to "userCountry = ANY(\"Countries\")").
            // A user with no country sees only region/global tournaments. Codes are canonical ISO codes.
            IQueryable<TournamentEntity> query = this.BaseDbSet().AsNoTracking();

            if (string.IsNullOrEmpty(userCountry))
            {
                query = query.Where(x => hubIds.Contains(x.HubId!.Value)
                    && x.Countries == null
                    && (x.Region == region || x.Region == RegionType.GLOBAL));
            }
            else
            {
                query = query.Where(x => hubIds.Contains(x.HubId!.Value)
                    && ((x.Countries == null && (x.Region == region || x.Region == RegionType.GLOBAL))
                        || (x.Countries != null && x.Countries.Contains(userCountry))));
            }

            switch (filter)
            {
                case TournamentUserStatus.AvailableToJoin:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.RegistrationOpen &&
                        !x.TournamentParticipants!.Any(tp =>
                            tp.UserId == userId ||
                            (tp.Team != null && tp.Team.Members.Any(tm => tm.UserId == userId))) &&
                        (x.RegistrationDeadline == null || x.RegistrationDeadline > DateTime.UtcNow)
                    );
                    break;

                case TournamentUserStatus.Upcoming:
                    query = query.Where(x =>
                        (x.Status == TournamentStatus.RegistrationOpen ||
                         x.Status == TournamentStatus.RegistrationClosed) &&
                        x.TournamentParticipants!.Any(tp =>
                            tp.UserId == userId ||
                            (tp.Team != null && tp.Team.Members.Any(tm => tm.UserId == userId)))
                    );
                    break;

                case TournamentUserStatus.Live:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.InProgress &&
                        x.TournamentParticipants!.Any(tp =>
                            tp.UserId == userId ||
                            (tp.Team != null && tp.Team.Members.Any(tm => tm.UserId == userId)))
                    );
                    break;

                case TournamentUserStatus.Completed:
                    query = query.Where(x =>
                        x.Status == TournamentStatus.Completed &&
                        x.TournamentParticipants!.Any(tp =>
                            tp.UserId == userId ||
                            (tp.Team != null && tp.Team.Members.Any(tm => tm.UserId == userId)))
                    );
                    break;

                default:
                    throw new ArgumentException($"Invalid filter: {filter}");
            }

            return query;
        }

        public async Task<int> GetNumberOfTournamentsWonByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                    .CountAsync(t => t.WinnerUserId == userId
                        || (t.WinnerTeamId != null && t.WinnerTeam!.Members.Any(m => m.UserId == userId)));
        }

        public async Task<bool> CheckIsUserIsRegistered(Guid id, Guid userId)
        {
            return await this.BaseDbSet()
                .AnyAsync(t => t.Id == id &&
                   (t.TournamentParticipants!.Any(tp => tp.UserId == userId) ||
                    t.TournamentRegistrations!.Any(tr => tr.UserId == userId)));
        }

        public async Task<TournamentEntity> GetWithHubById(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.Hub)
                .FirstAsync(x => x.Id == id);
        }

        public async Task<Guid?> GetHubOwnerUserId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.Id == tournamentId)
                .Select(t => t.Hub!.UserId)
                .FirstOrDefaultAsync();
        }

        public async Task<HubOwnershipInfo?> GetHubOwnership(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.Id == tournamentId && t.Hub != null)
                .Select(t => new HubOwnershipInfo
                {
                    HubId = t.Hub!.Id!.Value,
                    OwnerUserId = t.Hub!.UserId
                })
                .FirstOrDefaultAsync();
        }

        public async Task<TournamentApprovalContext?> GetApprovalContext(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.Id == tournamentId)
                .Select(t => new TournamentApprovalContext
                {
                    HubOwnerUserId = t.Hub!.UserId,
                    RequireResultApproval = t.RequireResultApproval
                })
                .FirstOrDefaultAsync();
        }
    }
}