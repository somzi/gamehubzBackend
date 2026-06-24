using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TeamMatchEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public Guid? TournamentStageId { get; set; }
        public TournamentStageEntity? TournamentStage { get; set; }
        public Guid? HomeTeamParticipantId { get; set; }
        public TournamentParticipantEntity? HomeTeamParticipant { get; set; }
        public Guid? AwayTeamParticipantId { get; set; }
        public TournamentParticipantEntity? AwayTeamParticipant { get; set; }
        public Guid? HomeTeamRepresentativeUserId { get; set; }
        public Guid? AwayTeamRepresentativeUserId { get; set; }
        public int? RoundNumber { get; set; }
        public int? MatchOrder { get; set; }
        public TeamMatchStatus Status { get; set; }
        public Guid? WinnerTeamParticipantId { get; set; }
        public Guid? NextTeamMatchId { get; set; }
        public TeamMatchEntity? NextTeamMatch { get; set; }

        // Loser drop-in target. Single-elimination: the third-place play-off (semi-final losers).
        // Double-elimination: the Losers Bracket match a Winners Bracket loser drops into.
        public Guid? NextTeamMatchLoserBracketId { get; set; }

        public TeamMatchEntity? NextTeamMatchLoserBracket { get; set; }

        // Marks the third-place play-off itself, so it is not mistaken for the championship final.
        public bool IsThirdPlace { get; set; }

        // False for matches in the Losers Bracket of a double-elimination tournament. Mirrors
        // MatchEntity.IsUpperBracket; lets the structure mapping tab/style LB matches distinctly.
        public bool IsUpperBracket { get; set; } = true;

        // Marks the double-elimination Grand Final. TeamMatchEntity has no Stage column (stage is
        // derived at DTO mapping time), so this explicit flag plays the role MatchStage.GrandFinal
        // does for solo matches — mirrors the existing IsThirdPlace flag.
        public bool IsGrandFinal { get; set; }

        // Marks the reset Grand Final — created only when the Losers Bracket champion wins the first
        // Grand Final, leaving both finalists with one loss. Maps to MatchStage.GrandFinalReset.
        public bool IsGrandFinalReset { get; set; }

        // Explicit destination slot overrides (0 = home, 1 = away). Default (null) preserves the
        // legacy MatchOrder%2 pairing used by single-elimination. Double-elimination sets these
        // because LB drop-in edges don't follow the bracket-pair convention (a WB loser entering an
        // LB match always takes the away slot; an LB winner advancing always takes home).
        public int? NextTeamMatchHomeAwaySlot { get; set; }
        public int? NextTeamMatchLoserBracketHomeAwaySlot { get; set; }

        public List<MatchEntity> SubMatches { get; set; } = new();
    }
}