using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Interfaces
{
    public interface ITeamJoinRequestRepository : IRepository<TeamJoinRequestEntity>
    {
        Task<List<TeamJoinRequestDto>> GetPendingRequestsByTeamId(Guid teamId);
        Task<TeamJoinRequestEntity?> GetPendingByTeamAndUser(Guid teamId, Guid userId);
        Task<TeamJoinRequestEntity?> GetByIdWithTeam(Guid requestId);
        Task<bool> HasPendingRequest(Guid teamId, Guid userId);
    }
}
