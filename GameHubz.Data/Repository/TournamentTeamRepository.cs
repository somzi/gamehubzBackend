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
    public class TournamentTeamRepository : BaseRepository<ApplicationContext, TournamentTeamEntity>, ITournamentTeamRepository
    {
        public TournamentTeamRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<TournamentTeamEntity>> GetByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .ToListAsync();
        }

        public async Task<List<TeamDto>> GetTeamsDtoByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId)
                .Select(t => new TeamDto
                {
                    TeamId = t.Id!.Value,
                    TeamName = t.TeamName,
                    CaptainUserId = t.CaptainUserId!.Value,
                    TeamSize = t.Tournament!.TeamSize,
                    MemberCount = t.Members.Count,
                    Members = t.Members.Select(m => new TeamMemberDto
                    {
                        UserId = m.UserId!.Value,
                        Username = m.User!.Username,
                        AvatarUrl = m.User.AvatarUrl
                    }).ToList()
                })
                .ToListAsync();
        }

        public async Task<List<TournamentTeamEntity>> GetFinalByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId && t.TournamentParticipantId != null)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .ToListAsync();
        }

        public async Task<TournamentTeamEntity?> GetByIdWithMembers(Guid teamId)
        {
            return await this.BaseDbSet()
                .Where(t => t.Id == teamId)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .Include(t => t.Tournament)
                .FirstOrDefaultAsync();
        }

        public async Task<TournamentTeamEntity> GetSingleByTournamentId(Guid tournamentId, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId
                    && t.Members.Any(m => m.UserId == userId))
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .Include(t => t.Tournament)
                    .ThenInclude(t => t!.TournamentRegistrations)
                .Include(t => t.Tournament)
                    .ThenInclude(t => t!.TournamentParticipants)
                .FirstAsync();
        }

        public async Task<TeamDto> GetTeamDtoByTournamentId(Guid tournamentId, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId && t.Members.Any(m => m.UserId == userId))
                .Select(t => new TeamDto
                {
                    TeamId = t.Id!.Value,
                    TeamName = t.TeamName,
                    CaptainUserId = t.CaptainUserId!.Value,
                    MemberCount = t.Members.Count,
                    TeamSize = t.Tournament!.TeamSize,
                    Members = t.Members.Select(m => new TeamMemberDto
                    {
                        UserId = m.UserId!.Value,
                        Username = m.User!.Username,
                        AvatarUrl = m.User.AvatarUrl
                    }).ToList(),
                    IsAlreadyRegistred = t.Tournament.TournamentRegistrations!.Any(r =>
                        r.TeamId == t.Id && r.Status == TournamentRegistrationStatus.Pending),
                    IsRegistrationAccepted = t.Tournament.TournamentRegistrations!.Any(r =>
                        r.TeamId == t.Id && r.Status == TournamentRegistrationStatus.Approved)
                })
                .FirstAsync();
        }
            public async Task<TeamJoinData?> GetTeamForJoin(Guid teamId, Guid userId)
            {
                var membersQuery = this.ContextBase.Set<TournamentTeamMemberEntity>();

                return await this.BaseDbSet()
                    .Where(t => t.Id == teamId)
                    .Select(t => new TeamJoinData
                    {
                        TeamId = t.Id!.Value,
                        TournamentId = t.TournamentId!.Value,
                        CaptainUserId = t.CaptainUserId!.Value,
                        TeamName = t.TeamName,
                        TeamSize = t.Tournament!.TeamSize,
                        CurrentMemberCount = t.Members.Count,
                        Members = t.Members.Select(m => new TeamMemberDto
                        {
                            UserId = m.UserId!.Value,
                            Username = m.User!.Username,
                            AvatarUrl = m.User.AvatarUrl
                        }).ToList(),
                        UserAlreadyInTournament = membersQuery.Any(m => m.UserId == userId && m.Team!.TournamentId == t.TournamentId)
                    })
                    .FirstOrDefaultAsync();
            }
        }
    }