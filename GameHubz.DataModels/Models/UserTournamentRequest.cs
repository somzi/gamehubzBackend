using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserTournamentRequest
    {
        public TournamentUserStatus Status { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; } = 10;
    }
}