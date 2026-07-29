using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentTeamMemberEntity : BaseEntity
    {
        public Guid? TeamId { get; set; }
        public TournamentTeamEntity? Team { get; set; }
        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }
        public DateTime? JoinedAt { get; set; }

        /// <summary>
        /// On the roster but out of the lineup — no sub-match is generated for a reserve. A team
        /// always carries exactly TeamSize starters; the captain trades a starter for a reserve via
        /// <see cref="GameHubz.Logic.Services.TournamentTeamService"/>.SwapLineupMember, which is
        /// always slot-for-slot so the random sub-match pairing can't be rearranged.
        /// False for every member of a tournament without reserves.
        /// </summary>
        public bool IsReserve { get; set; }
    }
}
