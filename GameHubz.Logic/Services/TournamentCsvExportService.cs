using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using System.Globalization;
using System.Text;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Machine-readable companion to <see cref="TournamentExportService"/>. Where the PDF is a
    /// document for people to read, this emits flat CSV tables meant to be fetched by a script and
    /// loaded straight into a website / spreadsheet: the standings (rankings) and the match results.
    ///
    /// Both datasets are built from the v3 tournament structure — the same snapshot the app renders
    /// — so a team tournament's group fixtures come out as one Team-vs-Team row per fixture instead
    /// of one row per player sub-match.
    /// </summary>
    public class TournamentCsvExportService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Excel only reads a UTF-8 CSV correctly when it starts with a BOM; without one, names with
        // Serbian / Cyrillic characters open as mojibake. Parsers on the ingest side either strip it
        // or expose a "bom" flag, so this is the safer default for a file that gets both uses.
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        // League round deadlines can carry a year-9999 sentinel meaning "opens after the previous
        // round" rather than a real play-by date. Same guard the PDF export applies.
        private const int SentinelYear = 9000;

        private readonly BracketService bracketService;

        public TournamentCsvExportService(BracketService bracketService)
        {
            this.bracketService = bracketService;
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public async Task<(byte[] Csv, string TournamentName)> GenerateCsvAsync(
            Guid tournamentId, TournamentCsvDataset dataset)
        {
            // v3 is cached by BracketService and invalidated on every result change, so the CSV is
            // exactly as fresh as what the app shows — no second cache layer needed here.
            var structure = await this.bracketService.GetTournamentStructureV3(tournamentId);

            string csv = dataset == TournamentCsvDataset.Matches
                ? BuildMatchesCsv(structure)
                : BuildStandingsCsv(structure);

            return (Encode(csv), structure.Name);
        }

        // ================================================================
        //  STANDINGS — one row per participant per group
        // ================================================================

        private static string BuildStandingsCsv(TournamentStructureDto s)
        {
            var sb = new StringBuilder(8192);

            WriteRow(sb,
                "tournamentId", "tournamentName", "format", "tournamentStatus", "isTeamTournament",
                "stageOrder", "stageName", "stageType", "groupName",
                "position", "qualified", "participantId", "userId", "name",
                "points", "played", "wins", "draws", "losses",
                "goalsFor", "goalsAgainst", "goalDifference", "buchholz");

            foreach (var stage in s.Stages.OrderBy(x => x.Order))
            {
                if (stage.Groups is not { Count: > 0 }) continue;

                foreach (var group in stage.Groups)
                {
                    foreach (var row in group.Standings.OrderBy(r => r.Position))
                    {
                        WriteRow(sb,
                            s.TournamentId.ToString(),
                            s.Name,
                            s.Format.ToString(),
                            s.Status.ToString(),
                            Bool(s.IsTeamTournament),
                            Num(stage.Order),
                            stage.Name,
                            stage.Type.ToString(),
                            group.Name,
                            Num(row.Position),
                            // Blank rather than "false" when the tournament defines no qualifying
                            // cut — "unknown" and "did not qualify" are different answers.
                            s.QualifiersPerGroup is { } q ? Bool(row.Position <= q) : "",
                            row.ParticipantId.ToString(),
                            row.UserId.ToString(),
                            row.Name,
                            Num(row.Points),
                            Num(row.MatchesPlayed),
                            Num(row.Wins),
                            Num(row.Draws),
                            Num(row.Losses),
                            Num(row.GoalsFor),
                            Num(row.GoalsAgainst),
                            Num(row.GoalDifference),
                            // Swiss-only tiebreaker; empty on every other stage type.
                            row.OpponentPointsSum is { } b ? Num(b) : "");
                    }
                }
            }

            return sb.ToString();
        }

        // ================================================================
        //  MATCHES — one row per fixture, across every stage
        // ================================================================

        private static string BuildMatchesCsv(TournamentStructureDto s)
        {
            var sb = new StringBuilder(16384);

            WriteRow(sb,
                "tournamentId", "tournamentName", "format", "tournamentStatus", "isTeamTournament",
                "stageOrder", "stageName", "stageType", "groupName",
                "round", "roundLabel", "matchOrder", "matchId", "matchStatus", "outcome",
                "startTimeUtc", "roundDeadlineUtc", "bestOf",
                "homeParticipantId", "homeUserId", "homeName", "homeScore",
                "awayParticipantId", "awayUserId", "awayName", "awayScore",
                "winnerParticipantId", "winnerName",
                "games", "tiebreakGames");

            foreach (var stage in s.Stages.OrderBy(x => x.Order))
            {
                if (stage.Rounds is { Count: > 0 })
                {
                    foreach (var round in stage.Rounds.OrderBy(r => r.RoundNumber))
                    {
                        string label = ResolveRoundLabel(round);
                        foreach (var m in round.Matches.OrderBy(m => m.Order))
                            WriteMatchRow(sb, s, stage, groupName: "", label, m);
                    }
                }

                if (stage.Groups is { Count: > 0 })
                {
                    foreach (var group in stage.Groups)
                    {
                        foreach (var m in group.Matches.OrderBy(m => m.Round).ThenBy(m => m.Order))
                            WriteMatchRow(sb, s, stage, group.Name, $"Round {m.Round}", m);
                    }
                }
            }

            return sb.ToString();
        }

        // One fixture. In a team tournament every row is a Team-vs-Team fixture, so matchId is the
        // team match's id and the home/away ids are the team participants — never a player
        // sub-match. Solo tournaments report the plain match id.
        private static void WriteMatchRow(
            StringBuilder sb,
            TournamentStructureDto s,
            TournamentStageStructureDto stage,
            string groupName,
            string roundLabel,
            MatchStructureDto m)
        {
            bool isTeam = s.IsTeamTournament;
            var winner = m.Home?.IsWinner == true ? m.Home : m.Away?.IsWinner == true ? m.Away : null;

            WriteRow(sb,
                s.TournamentId.ToString(),
                s.Name,
                s.Format.ToString(),
                s.Status.ToString(),
                Bool(isTeam),
                Num(stage.Order),
                stage.Name,
                stage.Type.ToString(),
                groupName,
                Num(m.Round),
                roundLabel,
                Num(m.Order),
                m.Id.ToString(),
                m.Status.ToString(),
                ResolveOutcome(m, stage.Type),
                Timestamp(m.StartTime),
                Timestamp(m.RoundDeadline),
                Num(m.BestOf),
                m.Home?.ParticipantId.ToString() ?? "",
                m.Home?.UserId.ToString() ?? "",
                Name(m.Home, isTeam),
                Score(m.Home),
                m.Away?.ParticipantId.ToString() ?? "",
                m.Away?.UserId.ToString() ?? "",
                Name(m.Away, isTeam),
                Score(m.Away),
                winner?.ParticipantId.ToString() ?? "",
                winner == null ? "" : Name(winner, isTeam),
                FormatGames(m.Games, tiebreak: false),
                FormatGames(m.Games, tiebreak: true));
        }

        // How the fixture actually ended, collapsed into one machine-friendly token. Deliberately
        // more specific than the winner columns, which cannot distinguish the three different ways
        // a match can be Completed with nobody flagged as winner.
        private static string ResolveOutcome(MatchStructureDto m, StageType stageType)
        {
            // Both sides forfeited a group / league / Swiss fixture: recorded, nobody scores.
            if (m.Status == MatchStatus.NoShow) return "double_forfeit";

            // Reported level in a solo knockout — waiting on a tiebreak series, not yet decided.
            if (m.Status == MatchStatus.TieBreakRequired) return "tiebreak_pending";

            if (m.Status != MatchStatus.Completed) return "pending";

            // A completed one-sided match is a bye — a Swiss sit-out or an odd-bracket free pass.
            if (m.Home == null || m.Away == null) return "bye";

            if (m.Home.IsWinner) return "home";
            if (m.Away.IsWinner) return "away";

            // Completed with neither side winning means a draw in a table-based stage, but in a
            // bracket it is the double-walkover case: both sides out, nobody advances.
            return IsTableStage(stageType) ? "draw" : "double_walkover";
        }

        private static bool IsTableStage(StageType t)
            => t is StageType.GroupStage or StageType.League or StageType.Swiss;

        // Individual game scores of a Best-of series, "3-1|2-0". SeriesNumber 1 is the main series;
        // anything above it is a replayed tiebreak, which gets its own column so the two never mix.
        private static string FormatGames(List<SeriesGame>? games, bool tiebreak)
        {
            if (games is not { Count: > 0 }) return "";

            var selected = games.Where(g => tiebreak ? g.SeriesNumber > 1 : g.SeriesNumber <= 1).ToList();
            if (selected.Count == 0) return "";

            return string.Join("|", selected.Select(g => $"{g.HomeScore}-{g.AwayScore}"));
        }

        // Mirrors the PDF export's round naming: prefer the meaningful late-stage label
        // (Final / Semifinal / …) over the generic "Round N" the DTO carries by default.
        private static string ResolveRoundLabel(BracketRoundDto round)
        {
            if (round.Matches.Count == 0) return round.Name;

            return round.Matches[0].Stage switch
            {
                MatchStage.GrandFinal => "Grand Final",
                MatchStage.GrandFinalReset => "Grand Final (Reset)",
                MatchStage.Final => "Final",
                MatchStage.SemiFinal => "Semifinal",
                MatchStage.QuarterFinal => "Quarterfinal",
                MatchStage.RoundOf16 => "Round of 16",
                MatchStage.RoundOf32 => "Round of 32",
                MatchStage.RoundOf64 => "Round of 64",
                _ => round.Name,
            };
        }

        // ================================================================
        //  CSV PRIMITIVES (RFC 4180)
        // ================================================================

        private static void WriteRow(StringBuilder sb, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(cells[i]));
            }
            sb.Append("\r\n");
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                || value[0] == ' '
                || value[^1] == ' ';

            return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static string Name(MatchParticipantDto? p, bool isTeam)
        {
            if (p == null) return "";
            return isTeam ? (p.TeamName ?? p.Username) : p.Username;
        }

        // Left empty rather than zeroed when a match has no reported score — an unplayed fixture
        // and a genuine 0 must not read the same downstream.
        private static string Score(MatchParticipantDto? p)
            => p?.Score is { } score ? Num(score) : "";

        private static string Num(int value) => value.ToString(Inv);

        private static string Bool(bool value) => value ? "true" : "false";

        // ISO-8601. Stored times are already UTC, so the value is stamped rather than converted —
        // same treatment the PDF export gives them.
        private static string Timestamp(DateTime? value)
        {
            if (value is not { } dt || dt.Year > SentinelYear) return "";
            return dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);
        }

        private static byte[] Encode(string csv)
        {
            var body = Encoding.UTF8.GetBytes(csv);
            var result = new byte[Utf8Bom.Length + body.Length];
            Utf8Bom.CopyTo(result, 0);
            body.CopyTo(result, Utf8Bom.Length);
            return result;
        }
    }
}
