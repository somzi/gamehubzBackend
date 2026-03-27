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
                   MemberCount = x.TeamId.HasValue ? x.Team!.Members.Count() : null
               })
               .ToListAsync();
        }

        public Task<TournamentRegistrationEntity> GetUserByTournamentId(Guid tournamentId, Guid userId)
        {
            return this.BaseDbSet()
                .FirstAsync(tp => tp.TournamentId == tournamentId && tp.UserId == userId);
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