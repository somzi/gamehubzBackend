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
    public class TeamJoinRequestRepository : BaseRepository<ApplicationContext, TeamJoinRequestEntity>, ITeamJoinRequestRepository
    {
        public TeamJoinRequestRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<TeamJoinRequestDto>> GetPendingRequestsByTeamId(Guid teamId)
        {
            return await this.BaseDbSet()
                .Where(r => r.TeamId == teamId && r.Status == JoinRequestStatus.Pending)
                .Select(r => new TeamJoinRequestDto
                {
                    RequestId = r.Id!.Value,
                    UserId = r.UserId!.Value,
                    Username = r.User!.Username,
                    AvatarUrl = r.User.AvatarUrl,
                    RequestedAt = r.CreatedOn!.Value
                })
                .ToListAsync();
        }

        public async Task<TeamJoinRequestEntity?> GetPendingByTeamAndUser(Guid teamId, Guid userId)
        {
            return await this.BaseDbSet()
                .FirstOrDefaultAsync(r => r.TeamId == teamId && r.UserId == userId && r.Status == JoinRequestStatus.Pending);
        }

        public async Task<TeamJoinRequestEntity?> GetApprovedByTeamAndUser(Guid teamId, Guid userId)
        {
            return await this.BaseDbSet()
                .FirstOrDefaultAsync(r => r.TeamId == teamId && r.UserId == userId && r.Status == JoinRequestStatus.Approved);
        }

        public async Task<TeamJoinRequestEntity?> GetByIdWithTeam(Guid requestId)
        {
            return await this.BaseDbSet()
                .Where(r => r.Id == requestId)
                .Include(r => r.Team)
                    .ThenInclude(t => t!.Members)
                .Include(r => r.User)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> HasPendingRequest(Guid teamId, Guid userId)
        {
            return await this.BaseDbSet()
                .AnyAsync(r => r.TeamId == teamId && r.UserId == userId && r.Status == JoinRequestStatus.Pending);
        }
    }
}
