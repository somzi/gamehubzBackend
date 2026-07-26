using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Everything the organiser's draw picker needs before a bracket exists: which seeding modes this
    /// format supports, the shape it has to fill (bracket size / byes, or groups and their size), and
    /// the entrants to place. Manager-only — it exposes the full entrant list of a tournament whose
    /// bracket has not been drawn yet.
    /// </summary>
    public class BracketDrawOptionsDto
    {
        public Guid TournamentId { get; set; }
        public TournamentFormat Format { get; set; }
        public bool IsTeamTournament { get; set; }

        /// <summary>Number of entrants: players for solo tournaments, teams for team tournaments.</summary>
        public int EntrantCount { get; set; }

        /// <summary>Elimination formats: entrant count rounded up to a power of two. Null otherwise.</summary>
        public int? BracketSize { get; set; }

        /// <summary>Elimination formats: <see cref="BracketSize"/> - <see cref="EntrantCount"/>.</summary>
        public int? ByeCount { get; set; }

        /// <summary>Group formats: how many groups the entrants are split into. Null otherwise.</summary>
        public int? GroupsCount { get; set; }

        /// <summary>Group formats: how many entrants advance out of each group.</summary>
        public int? QualifiersPerGroup { get; set; }

        /// <summary>
        /// Group formats: how many pots a pot draw uses — the largest group size, i.e.
        /// ceil(EntrantCount / GroupsCount). Null otherwise.
        /// </summary>
        public int? PotCount { get; set; }

        /// <summary>
        /// The seeding modes the organiser may pick for this format. Always contains
        /// <see cref="BracketSeedingMode.Random"/>.
        /// </summary>
        public List<BracketSeedingMode> SupportedModes { get; set; } = new();

        /// <summary>Entrants in registration order (the order the Seeded mode uses).</summary>
        public List<BracketDrawEntrantDto> Entrants { get; set; } = new();
    }

    public class BracketDrawEntrantDto
    {
        /// <summary>TournamentParticipant id — the id a draw plan refers to.</summary>
        public Guid ParticipantId { get; set; }

        /// <summary>Set for solo entrants; null for teams.</summary>
        public Guid? UserId { get; set; }

        /// <summary>Set for team entrants; null for solo.</summary>
        public Guid? TeamId { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        /// <summary>Existing seed, when one has already been assigned. Usually null before generation.</summary>
        public int? Seed { get; set; }
    }
}
