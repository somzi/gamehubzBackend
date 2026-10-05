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
    public class TournamentPlayerDeviceRepository : BaseRepository<ApplicationContext, TournamentPlayerDeviceEntity>, ITournamentPlayerDeviceRepository
    {
        public TournamentPlayerDeviceRepository(ApplicationContext context, DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder, ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService) { }

        public async Task LockPlayer(Guid tournamentId, Guid userId)
        {
            // Inside the caller's transaction. Also protects the very first binding, when no row
            // exists to lock. Concurrent enrollments and decisions serialize across API instances.
            if (this.ContextBase.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                string key = $"tournament-phone:{tournamentId:D}:{userId:D}";
                await this.ContextBase.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
            }
        }

        public Task<List<TournamentPlayerDeviceEntity>> GetForPlayer(Guid tournamentId, Guid userId) =>
            this.BaseDbSet().Include(x => x.UserDevice)
                .Where(x => x.TournamentId == tournamentId && x.UserId == userId).ToListAsync();

        public Task<List<TournamentPlayerDeviceEntity>> GetPending(Guid tournamentId) =>
            this.BaseDbSet().AsNoTracking().Include(x => x.User).Include(x => x.UserDevice)
                .Where(x => x.TournamentId == tournamentId && x.Status == TournamentPlayerDeviceStatus.Pending)
                .OrderBy(x => x.RequestedOn).ToListAsync();

        public async Task<bool> IsRegistered(Guid tournamentId, Guid userId)
        {
            if (await this.ContextBase.Set<TournamentRegistrationEntity>().AnyAsync(x =>
                x.TournamentId == tournamentId && x.UserId == userId && x.Status != TournamentRegistrationStatus.Rejected)) return true;
            if (await this.ContextBase.Set<TournamentParticipantEntity>().AnyAsync(x =>
                x.TournamentId == tournamentId && x.UserId == userId)) return true;
            return await this.ContextBase.Set<TournamentTeamEntity>().AnyAsync(x =>
                x.TournamentId == tournamentId && (x.CaptainUserId == userId || x.Members.Any(m => m.UserId == userId)));
        }

        public Task<List<TournamentCountRow>> GetPendingCountsByTournament(List<Guid> hubIds) =>
            this.BaseDbSet().Where(x => x.Status == TournamentPlayerDeviceStatus.Pending
                && x.Tournament!.RequireResultVerification && x.Tournament.HubId != null
                && hubIds.Contains(x.Tournament.HubId.Value))
            .GroupBy(x => new { x.TournamentId, HubId = x.Tournament!.HubId!.Value, Status = (int)x.Tournament.Status })
            .Select(g => new TournamentCountRow { TournamentId = g.Key.TournamentId, HubId = g.Key.HubId,
                Status = g.Key.Status, Count = g.Count() }).ToListAsync();
    }
}
