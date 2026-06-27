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
        /// True when the requesting user may perform owner-level actions (hub owner, hub admin or
        /// platform admin). Only populated by the v2 structure endpoint; omitted from the v1 payload
        /// so the legacy client keeps receiving an unchanged response.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool CanManage { get; set; }
    }
}