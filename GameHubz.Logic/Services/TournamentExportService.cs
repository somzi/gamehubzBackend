using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text;

namespace GameHubz.Logic.Services
{
    public class TournamentExportService
    {
        // ── Layout constants (points) ──────────────────────────────────
        private const float MatchBoxW = 174;

        private const float MatchBoxH = 50;
        private const float RowH = 25;
        private const float RoundGap = 56;
        private const float ColW = MatchBoxW + RoundGap;
        private const float BaseGapY = 14;
        private const float BoxR = 6;
        private const float ScoreBadgeW = 28;
        private const float ScoreBadgeH = 16;
        private const float ConnStroke = 1.6f;
        private const float AccentW = 4;
        private const float Pad = 30;
        private const float HeaderH = 82;
        private const float RoundLblH = 28;
        private const float FooterH = 28;
        private const int MaxNameLen = 18;

        // ── Colors ─────────────────────────────────────────────────────
        private const string CHeaderBg = "#0F172A";

        private const string CAccent = "#3B82F6";
        private const string CBoxBg = "#F8F9FA";
        private const string CBoxBrd = "#DEE2E6";
        private const string CWinBg = "#D4EDDA";
        private const string CWinBar = "#28A745";
        private const string CWinTxt = "#155724";
        private const string CTxt = "#343A40";
        private const string CTbd = "#ADB5BD";
        private const string CSeed = "#94A3B8";
        private const string CSep = "#E9ECEF";
        private const string CConn = "#CBD5E1";
        private const string CRndBg = "#1E293B";
        private const string CScoreW = "#28A745";
        private const string CScoreL = "#6C757D";
        private const string CMuted = "#94A3B8";
        private const string CWhite = "#FFFFFF";
        private const string Font = "Inter";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly BracketService bracketService;
        private readonly ICacheService cacheService;

        public TournamentExportService(BracketService bracketService, ICacheService cacheService)
        {
            this.bracketService = bracketService;
            this.cacheService = cacheService;
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public async Task<(byte[] Pdf, string TournamentName)> GenerateBracketPdfAsync(Guid tournamentId)
        {
            string cacheKey = $"pdf:bracket:{tournamentId}";

            var structure = await this.bracketService.GetTournamentStructure(tournamentId);

            var cached = await this.cacheService.GetAsync<byte[]>(cacheKey);
            if (cached != null)
                return (cached, structure.Name);

            var document = Document.Create(doc =>
            {
                foreach (var stage in structure.Stages)
                {
                    if (stage.Rounds is { Count: > 0 })
                        CreateBracketPage(doc, structure, stage);

                    if (stage.Groups is { Count: > 0 })
                        CreateGroupPage(doc, structure, stage);
                }
            });

            var pdf = document.GeneratePdf();
            await this.cacheService.SetAsync(cacheKey, pdf, TimeSpan.FromMinutes(30));
            return (pdf, structure.Name);
        }

        // ================================================================
        //  BRACKET PAGE — SVG-based rendering
        // ================================================================

        private static void CreateBracketPage(
            IDocumentContainer doc,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage)
        {
            var rounds = stage.Rounds!;
            int r1Count = rounds[0].Matches.Count;

            float bracketW = rounds.Count * ColW - RoundGap;
            float bracketH = r1Count * (MatchBoxH + BaseGapY) - BaseGapY;
            float pageW = Math.Max(bracketW + Pad * 2, 842);
            float pageH = Math.Max(HeaderH + RoundLblH + bracketH + FooterH + 30, 420);

            string svg = BuildSvg(pageW, pageH, structure, stage, rounds);

            doc.Page(page =>
            {
                page.Size(pageW, pageH);
                page.Margin(0);
                page.Content().Svg(SvgImage.FromText(svg));
            });
        }

        // ── SVG builder ────────────────────────────────────────────────

        private static string BuildSvg(
            float w, float h,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage,
            List<BracketRoundDto> rounds)
        {
            var sb = new StringBuilder(4096);

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{P(w)}\" height=\"{P(h)}\" viewBox=\"0 0 {P(w)} {P(h)}\">");

            // Defs
            sb.Append("<defs>");
            sb.Append("<filter id=\"sh\" x=\"-4%\" y=\"-4%\" width=\"112%\" height=\"120%\">");
            sb.Append("<feGaussianBlur in=\"SourceAlpha\" stdDeviation=\"2\" result=\"b\"/>");
            sb.Append("<feOffset in=\"b\" dx=\"1\" dy=\"2\" result=\"o\"/>");
            sb.Append("<feFlood flood-color=\"#000\" flood-opacity=\"0.07\" result=\"c\"/>");
            sb.Append("<feComposite in=\"c\" in2=\"o\" operator=\"in\" result=\"s\"/>");
            sb.Append("<feMerge><feMergeNode in=\"s\"/><feMergeNode in=\"SourceGraphic\"/></feMerge>");
            sb.Append("</filter>");
            // Clip paths for match boxes
            int idx = 0;
            foreach (var round in rounds)
                foreach (var _ in round.Matches)
                {
                    sb.Append($"<clipPath id=\"cp{idx}\"><rect x=\"0\" y=\"0\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\"/></clipPath>");
                    idx++;
                }
            sb.Append("</defs>");

            // ── Header ─────────────────────────────────────────────
            sb.Append($"<rect width=\"{P(w)}\" height=\"{P(HeaderH)}\" fill=\"{CHeaderBg}\"/>");
            sb.Append($"<line x1=\"0\" y1=\"{P(HeaderH)}\" x2=\"{P(w)}\" y2=\"{P(HeaderH)}\" stroke=\"{CAccent}\" stroke-width=\"3\"/>");
            sb.Append($"<text x=\"{P(Pad)}\" y=\"38\" fill=\"{CWhite}\" font-size=\"22\" font-weight=\"bold\" font-family=\"{Font}\">{Esc(structure.Name)}</text>");

            string sub = $"{structure.Format}  \u2022  {(structure.IsTeamTournament ? "Teams" : "Solo")}  \u2022  {stage.Name}";
            sb.Append($"<text x=\"{P(Pad)}\" y=\"58\" fill=\"{CMuted}\" font-size=\"11\" font-family=\"{Font}\">{Esc(sub)}</text>");

            // Status badge
            string statusTxt = structure.Status.ToString().ToUpper();
            string statusCol = structure.Status switch
            {
                TournamentStatus.InProgress => "#22C55E",
                TournamentStatus.Completed => CAccent,
                _ => CMuted,
            };
            float stW = statusTxt.Length * 6.5f + 16;
            float stX = w - stW - 30;
            sb.Append($"<rect x=\"{P(stX)}\" y=\"24\" width=\"{P(stW)}\" height=\"18\" rx=\"4\" fill=\"{statusCol}\" fill-opacity=\"0.12\"/>");
            sb.Append($"<rect x=\"{P(stX)}\" y=\"24\" width=\"{P(stW)}\" height=\"18\" rx=\"4\" fill=\"none\" stroke=\"{statusCol}\"/>");
            sb.Append($"<text x=\"{P(stX + 8)}\" y=\"37\" fill=\"{statusCol}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\">{Esc(statusTxt)}</text>");

            // ── Round labels ───────────────────────────────────────
            float rlY = HeaderH + 6;
            for (int i = 0; i < rounds.Count; i++)
            {
                float cx = Pad + i * ColW + MatchBoxW / 2;
                string lbl = rounds[i].Name;
                float bw = lbl.Length * 6f + 22;
                sb.Append($"<rect x=\"{P(cx - bw / 2)}\" y=\"{P(rlY)}\" width=\"{P(bw)}\" height=\"22\" rx=\"11\" fill=\"{CRndBg}\"/>");
                sb.Append($"<text x=\"{P(cx)}\" y=\"{P(rlY + 15)}\" fill=\"{CWhite}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"middle\">{Esc(lbl)}</text>");
            }

            // ── Positions ──────────────────────────────────────────
            float bTop = rlY + RoundLblH + 4;
            var positions = CalcPositions(rounds, Pad, bTop);

            // ── Connectors ─────────────────────────────────────────
            sb.Append($"<g stroke=\"{CConn}\" stroke-width=\"{P(ConnStroke)}\" stroke-linecap=\"round\" fill=\"none\">");
            for (int r = 0; r < rounds.Count - 1; r++)
            {
                float srcR = Pad + r * ColW + MatchBoxW;
                float midX = srcR + RoundGap / 2;
                float dstL = Pad + (r + 1) * ColW;
                int next = rounds[r + 1].Matches.Count;

                for (int j = 0; j < next; j++)
                {
                    int a = j * 2, b = a + 1;
                    if (a >= positions[r].Count) continue;
                    float y1 = positions[r][a];
                    float y2 = b < positions[r].Count ? positions[r][b] : y1;
                    float yD = positions[r + 1][j];

                    sb.Append($"<line x1=\"{P(srcR)}\" y1=\"{P(y1)}\" x2=\"{P(midX)}\" y2=\"{P(y1)}\"/>");
                    if (b < positions[r].Count)
                        sb.Append($"<line x1=\"{P(srcR)}\" y1=\"{P(y2)}\" x2=\"{P(midX)}\" y2=\"{P(y2)}\"/>");
                    sb.Append($"<line x1=\"{P(midX)}\" y1=\"{P(y1)}\" x2=\"{P(midX)}\" y2=\"{P(y2)}\"/>");
                    sb.Append($"<line x1=\"{P(midX)}\" y1=\"{P(yD)}\" x2=\"{P(dstL)}\" y2=\"{P(yD)}\"/>");
                }
            }
            sb.Append("</g>");

            // ── Match boxes ────────────────────────────────────────
            int ci = 0;
            for (int r = 0; r < rounds.Count; r++)
            {
                float x = Pad + r * ColW;
                for (int m = 0; m < rounds[r].Matches.Count && m < positions[r].Count; m++)
                {
                    float cy = positions[r][m];
                    float by = cy - MatchBoxH / 2;
                    AppendMatchBox(sb, x, by, rounds[r].Matches[m], structure.IsTeamTournament, ci++);
                }
            }

            // ── Footer ─────────────────────────────────────────────
            float fY = h - FooterH;
            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"{P(fY)}\" x2=\"{P(w - Pad)}\" y2=\"{P(fY)}\" stroke=\"#E2E8F0\"/>");
            string ts = $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  \u2022  GameHubz";
            sb.Append($"<text x=\"{P(Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"8\" font-family=\"{Font}\">{Esc(ts)}</text>");
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"8\" font-family=\"{Font}\" text-anchor=\"end\">gamehubz.com</text>");

            sb.Append("</svg>");
            return sb.ToString();
        }

        // ── Match box SVG ──────────────────────────────────────────────

        private static void AppendMatchBox(
            StringBuilder sb, float x, float y,
            MatchStructureDto match, bool isTeam, int clipIdx)
        {
            string home = Trunc(GetName(match.Home, isTeam));
            string away = Trunc(GetName(match.Away, isTeam));
            bool hw = match.Home?.IsWinner == true;
            bool aw = match.Away?.IsWinner == true;
            bool done = match.Status == MatchStatus.Completed;
            bool hasWinner = hw || aw;

            // Container group with shadow filter
            sb.Append($"<g filter=\"url(#sh)\">");

            // Box background
            sb.Append($"<rect x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"{CBoxBg}\"/>");

            // Clipped group for winner highlights + accent bar
            sb.Append($"<g transform=\"translate({P(x)},{P(y)})\" clip-path=\"url(#cp{clipIdx})\">");
            if (hw)
                sb.Append($"<rect width=\"{P(MatchBoxW)}\" height=\"{P(RowH)}\" fill=\"{CWinBg}\"/>");
            if (aw)
                sb.Append($"<rect y=\"{P(RowH)}\" width=\"{P(MatchBoxW)}\" height=\"{P(RowH)}\" fill=\"{CWinBg}\"/>");
            if (hasWinner)
                sb.Append($"<rect width=\"{P(AccentW)}\" height=\"{P(MatchBoxH)}\" fill=\"{CWinBar}\"/>");
            sb.Append("</g>");

            // Border
            sb.Append($"<rect x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"none\" stroke=\"{CBoxBrd}\"/>");

            // Separator line
            sb.Append($"<line x1=\"{P(x + 8)}\" y1=\"{P(y + RowH)}\" x2=\"{P(x + MatchBoxW - 8)}\" y2=\"{P(y + RowH)}\" stroke=\"{CSep}\"/>");

            // Participant rows
            AppendRow(sb, x, y, home, match.Home?.Score, match.Home?.Seed, hw, match.Home == null, done);
            AppendRow(sb, x, y + RowH, away, match.Away?.Score, match.Away?.Seed, aw, match.Away == null, done);

            sb.Append("</g>");
        }

        private static void AppendRow(
            StringBuilder sb, float bx, float ry,
            string name, int? score, int? seed,
            bool win, bool tbd, bool done)
        {
            float tx = bx + 10;
            float ty = ry + 17;

            // Seed
            if (seed.HasValue)
            {
                sb.Append($"<text x=\"{P(tx)}\" y=\"{P(ty)}\" fill=\"{CSeed}\" font-size=\"8\" font-family=\"{Font}\">{seed.Value}</text>");
                tx += 16;
            }

            // Name
            string col = tbd ? CTbd : win ? CWinTxt : CTxt;
            string bold = win ? " font-weight=\"bold\"" : "";
            sb.Append($"<text x=\"{P(tx)}\" y=\"{P(ty)}\" fill=\"{col}\" font-size=\"10\"{bold} font-family=\"{Font}\">{Esc(name)}</text>");

            // Score badge
            if (done && score != null)
            {
                string bg = win ? CScoreW : CScoreL;
                float sx = bx + MatchBoxW - ScoreBadgeW - 7;
                float sy = ry + (RowH - ScoreBadgeH) / 2;
                sb.Append($"<rect x=\"{P(sx)}\" y=\"{P(sy)}\" width=\"{P(ScoreBadgeW)}\" height=\"{P(ScoreBadgeH)}\" rx=\"3\" fill=\"{bg}\"/>");
                sb.Append($"<text x=\"{P(sx + ScoreBadgeW / 2)}\" y=\"{P(sy + 12)}\" fill=\"{CWhite}\" font-size=\"10\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"middle\">{score.Value}</text>");
            }
        }

        // ── Position calculation ───────────────────────────────────────

        private static List<List<float>> CalcPositions(
            List<BracketRoundDto> rounds, float left, float top)
        {
            var pos = new List<List<float>>();

            int n = rounds[0].Matches.Count;
            var r0 = new List<float>(n);
            for (int i = 0; i < n; i++)
                r0.Add(top + i * (MatchBoxH + BaseGapY) + MatchBoxH / 2);
            pos.Add(r0);

            for (int r = 1; r < rounds.Count; r++)
            {
                var prev = pos[r - 1];
                int count = rounds[r].Matches.Count;
                var list = new List<float>(count);

                for (int j = 0; j < count; j++)
                {
                    int a = j * 2, b = a + 1;
                    if (b < prev.Count) list.Add((prev[a] + prev[b]) / 2);
                    else if (a < prev.Count) list.Add(prev[a]);
                    else list.Add(top + j * (MatchBoxH + BaseGapY) * MathF.Pow(2, r) + MatchBoxH / 2);
                }
                pos.Add(list);
            }

            return pos;
        }

        // ================================================================
        //  GROUP STAGE PAGE — QuestPDF fluent API
        // ================================================================

        private static void CreateGroupPage(
            IDocumentContainer doc,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage)
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Inter"));

                page.Header().Column(col =>
                {
                    col.Item().Background(Colors.Grey.Darken4).Padding(15).Column(h =>
                    {
                        h.Item().Text(structure.Name).FontSize(18).Bold().FontColor(Colors.White);
                        h.Item().Text($"{structure.Format}  \u2022  {stage.Name}")
                            .FontSize(10).FontColor(Colors.Grey.Lighten2);
                    });
                    col.Item().Height(3).Background(Colors.Blue.Medium);
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    foreach (var group in stage.Groups!)
                        col.Item().PaddingBottom(15).Element(e => RenderGroupTable(e, group));
                });

                page.Footer()
                    .BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(5)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  \u2022  GameHubz")
                            .FontSize(7).FontColor(Colors.Grey.Medium);
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.Span("Page ").FontSize(7).FontColor(Colors.Grey.Medium);
                            t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });
            });
        }

        private static void RenderGroupTable(IContainer container, GroupDto group)
        {
            container.Column(col =>
            {
                col.Item().Row(r =>
                {
                    r.AutoItem()
                        .Background(Colors.Grey.Darken3)
                        .Padding(5).PaddingLeft(12).PaddingRight(12)
                        .Text(group.Name).FontSize(11).Bold().FontColor(Colors.White);
                });

                col.Item().PaddingTop(5).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);
                        c.RelativeColumn(4);
                        c.ConstantColumn(32);
                        c.ConstantColumn(25);
                        c.ConstantColumn(25);
                        c.ConstantColumn(25);
                        c.ConstantColumn(28);
                        c.ConstantColumn(28);
                        c.ConstantColumn(28);
                    });

                    table.Header(h =>
                    {
                        var hs = TextStyle.Default.FontSize(8).Bold().FontColor(Colors.White);
                        var bg = Colors.Grey.Darken3;
                        h.Cell().Background(bg).Padding(4).Text("#").Style(hs);
                        h.Cell().Background(bg).Padding(4).Text("Player / Team").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("Pts").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("W").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("D").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("L").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("GF").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("GA").Style(hs);
                        h.Cell().Background(bg).Padding(4).AlignCenter().Text("GD").Style(hs);
                    });

                    foreach (var s in group.Standings)
                    {
                        var cs = TextStyle.Default.FontSize(8);
                        var bg = s.Position % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;
                        table.Cell().Background(bg).Padding(4).Text(s.Position.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).Text(s.Name).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.Points.ToString()).Style(cs).Bold();
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.Wins.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.Draws.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.Losses.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.GoalsFor.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.GoalsAgainst.ToString()).Style(cs);
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(s.GoalDifference.ToString()).Style(cs);
                    }
                });
            });
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private static string GetName(MatchParticipantDto? p, bool isTeam)
        {
            if (p == null) return "TBD";
            return isTeam ? (p.TeamName ?? p.Username) : p.Username;
        }

        private static string Trunc(string s)
            => s.Length <= MaxNameLen ? s : s[..(MaxNameLen - 1)] + "\u2026";

        private static string Esc(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static string P(float v) => v.ToString("0.#", Inv);
    }
}