using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TeamMatchRepository : BaseRepository<ApplicationContext, TeamMatchEntity>, ITeamMatchRepository
    {
        public TeamMatchRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<TeamMatchEntity?> GetByIdWithSubMatches(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.HomeParticipant)
                        .ThenInclude(p => p!.User)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.AwayParticipant)
                        .ThenInclude(p => p!.User)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.HomeUser)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.AwayUser)
                .Include(tm => tm.HomeTeamParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                            .ThenInclude(m => m.User)
                .Include(tm => tm.AwayTeamParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                            .ThenInclude(m => m.User)
                .FirstOrDefaultAsync();
        }

        public async Task<List<TeamMatchEntity>> GetByStageId(Guid stageId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.TournamentStageId == stageId)
                .Include(tm => tm.SubMatches)
                .ToListAsync();
        }

        public async Task<TeamMatchDetailsProjection?> GetDetailsProjection(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Select(tm => new TeamMatchDetailsProjection
                {
                    TeamMatchId = tm.Id!.Value,
                    Status = tm.Status,
                    WinnerTeamParticipantId = tm.WinnerTeamParticipantId,
                    HomeTeamParticipantId = tm.HomeTeamParticipantId,
                    AwayTeamParticipantId = tm.AwayTeamParticipantId,
                    HomeTeamRepresentativeUserId = tm.HomeTeamRepresentativeUserId,
                    AwayTeamRepresentativeUserId = tm.AwayTeamRepresentativeUserId,
                    MatchOrder = tm.MatchOrder,
                    HomeTeam = tm.HomeTeamParticipant != null && tm.HomeTeamParticipant.Team != null
                        ? new TeamMatchTeamProjection
                        {
                            TeamId = tm.HomeTeamParticipant.Team.Id!.Value,
                            TeamName = tm.HomeTeamParticipant.Team.TeamName,
                            CaptainUserId = tm.HomeTeamParticipant.Team.CaptainUserId,
                            Members = tm.HomeTeamParticipant.Team.Members
                                .Where(m => m.UserId.HasValue)
                                .Select(m => new TeamMemberDto
                                {
                                    UserId = m.UserId!.Value,
                                    Username = m.User != null ? m.User.Username : "Unknown",
                                    AvatarUrl = m.User != null ? m.User.AvatarUrl : null
                                }).ToList()
                        }
                        : null,
                    AwayTeam = tm.AwayTeamParticipant != null && tm.AwayTeamParticipant.Team != null
                        ? new TeamMatchTeamProjection
                        {
                            TeamId = tm.AwayTeamParticipant.Team.Id!.Value,
                            TeamName = tm.AwayTeamParticipant.Team.TeamName,
                            CaptainUserId = tm.AwayTeamParticipant.Team.CaptainUserId,
                            Members = tm.AwayTeamParticipant.Team.Members
                                .Where(m => m.UserId.HasValue)
                                .Select(m => new TeamMemberDto
                                {
                                    UserId = m.UserId!.Value,
                                    Username = m.User != null ? m.User.Username : "Unknown",
                                    AvatarUrl = m.User != null ? m.User.AvatarUrl : null
                                }).ToList()
                        }
                        : null,
                    SubMatches = tm.SubMatches.OrderBy(sm => sm.MatchOrder).Select(sm => new SubMatchProjection
                    {
                        MatchId = sm.Id!.Value,
                        MatchOrder = sm.MatchOrder,
                        Status = sm.Status,
                        HomeUserScore = sm.HomeUserScore,
                        AwayUserScore = sm.AwayUserScore,
                        WinnerParticipantId = sm.WinnerParticipantId,
                        HomeParticipantId = sm.HomeParticipantId,
                        AwayParticipantId = sm.AwayParticipantId,
                        HomeUserId = sm.HomeUserId ?? (sm.HomeParticipant != null ? sm.HomeParticipant.UserId : null),
                        AwayUserId = sm.AwayUserId ?? (sm.AwayParticipant != null ? sm.AwayParticipant.UserId : null),
                        HomeUsername = sm.HomeUser != null
                            ? sm.HomeUser.Username
                            : (sm.HomeParticipant != null && sm.HomeParticipant.User != null
                                ? sm.HomeParticipant.User.Username
                                : null),
                        AwayUsername = sm.AwayUser != null
                            ? sm.AwayUser.Username
                            : (sm.AwayParticipant != null && sm.AwayParticipant.User != null
                                ? sm.AwayParticipant.User.Username
                                : null),
                        HomeAvatarUrl = sm.HomeUser != null
                            ? sm.HomeUser.AvatarUrl
                            : (sm.HomeParticipant != null && sm.HomeParticipant.User != null
                                ? sm.HomeParticipant.User.AvatarUrl
                                : null),
                        AwayAvatarUrl = sm.AwayUser != null
                            ? sm.AwayUser.AvatarUrl
                            : (sm.AwayParticipant != null && sm.AwayParticipant.User != null
                                ? sm.AwayParticipant.User.AvatarUrl
                                : null),
                        Evidences = sm.MatchEvidences != null
                            ? sm.MatchEvidences.Select(e => e.Url!).ToList()
                            : new List<string>()
                    }).ToList()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<TieBreakProjection?> GetTieBreakProjection(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Select(tm => new TieBreakProjection
                {
                    TeamMatchId = tm.Id!.Value,
                    Status = tm.Status,
                    HomeTeamRepresentativeUserId = tm.HomeTeamRepresentativeUserId,
                    HomeRepresentativeUsername = tm.HomeTeamRepresentativeUserId.HasValue
                        ? ContextBase.Set<UserEntity>()
                            .Where(u => u.Id == tm.HomeTeamRepresentativeUserId)
                            .Select(u => u.Username)
                            .FirstOrDefault()
                        : null,
                    AwayTeamRepresentativeUserId = tm.AwayTeamRepresentativeUserId,
                    AwayRepresentativeUsername = tm.AwayTeamRepresentativeUserId.HasValue
                        ? ContextBase.Set<UserEntity>()
                            .Where(u => u.Id == tm.AwayTeamRepresentativeUserId)
                            .Select(u => u.Username)
                            .FirstOrDefault()
                        : null
                })
                .FirstOrDefaultAsync();
        }
    }
}