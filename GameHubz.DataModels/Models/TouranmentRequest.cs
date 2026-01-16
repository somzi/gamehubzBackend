using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentRequest
    {
        public TournamentStatus Status { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; } = 10;
    }
}