using System.Text.Json.Serialization;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentStructureDto
    {
        public Guid TournamentId { get; set; }
        public string Name { get; set; } = "";
        public TournamentFormat Format { get; set; }
        public TournamentStatus Status { get; set; }
        public List<TournamentStageStructureDto> Stages { get; set; } = new();
        public Guid HubOwnerId { get; set; }
        public bool IsTeamTournament { get; set; }
        public int? QualifiersPerGroup { get; set; }

        /// <summary>
        /// Mirrors <see cref="TournamentEntity.RequireResultApproval"/>. Lets the client decide
        /// whether a participant's result submission is a final report or just a proposal.
        /// </summary>
        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// Mirrors <see cref="TournamentEntity.RequireMatchCheckIn"/>, with the grace window, so a
        /// bracket card can render the ready check without a second request.
        /// </summary>
        public bool RequireMatchCheckIn { get; set; }

        /// <summary>Grace minutes for the ready check. Null = the system default.</summary>
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>
        /// Tournament default series format. Every <see cref="MatchStructureDto"/> already carries
        /// its own resolved Best-of, so these are here for the organizer surfaces (round editor,
        /// format labels) rather than for rendering a card.
        /// </summary>
        public int BestOf { get; set; } = 1;

        public TeamWinCondition SeriesWinCondition { get; set; }

        public int? TiebreakBestOf { get; set; }

        /// <summary>Knockout-phase Best-of of a two-phase tournament. Null = same as <see cref="BestOf"/>.</summary>
        public int? KnockoutBestOf { get; set; }

        /// <summary>
        /// True when the requesting user may perform owner-level actions (hub owner, hub admin or
        /// platform admin). Only populated by the v2 structure endpoint; omitted from the v1 payload
        /// so the legacy client keeps receiving an unchanged response.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool CanManage { get; set; }
    }
}