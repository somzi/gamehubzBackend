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
        private const float MatchBoxW = 260;

        private const float MatchBoxH = 72;
        private const float RowH = 36;
        private const float RoundGap = 80;
        private const float ColW = MatchBoxW + RoundGap;
        private const float BaseGapY = 20;
        private const float BoxR = 8;
        private const float ScoreBadgeW = 40;
        private const float ScoreBadgeH = 22;
        private const float ConnStroke = 3.5f;
        private const float AccentW = 5;
        private const float Pad = 30;
        private const float HeaderH = 110;
        private const float RoundLblH = 28;
        private const float FooterH = 28;
        private const int MaxNameLen = 22;

        // ── Colors ─────────────────────────────────────────────────────
        private const string CHeaderBg = "#0F172A";

        private const string CAccent = "#3B82F6";
        private const string CBoxBg = "#F8F9FA";
        private const string CBoxBrd = "#DEE2E6";
        private const string CWinBg = "#EFF6FF";
        private const string CWinBar = "#3B82F6";
        private const string CWinTxt = "#1E3A8A";
        private const string CTxt = "#343A40";
        private const string CTbd = "#ADB5BD";
        private const string CSeed = "#94A3B8";
        private const string CSep = "#E9ECEF";
        private const string CConn = "#64748B";
        private const string CRndBg = "#1E293B";
        private const string CScoreW = "#3B82F6";
        private const string CScoreL = "#94A3B8";
        private const string CMuted = "#94A3B8";
        private const string CWhite = "#FFFFFF";

        // Group card palette — clean, white-card design inspired by the live group-stage UI.
        private const string CCardBg = "#FFFFFF";

        private const string CCardBrd = "#E5E7EB";
        private const string CGroupName = "#0F172A";
        private const string CTblHdr = "#94A3B8";
        private const string CRowTxt = "#1F2937";
        private const string CRowMuted = "#9CA3AF";
        private const string CQualifierBg = "#EFF6FF";
        private const string CGdPos = "#16A34A";
        private const string CGdNeg = "#DC2626";
        private const string CGdNeut = "#6B7280";

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

            // Check PDF cache first — avoids loading tournament structure at all when warm
            var cached = await this.cacheService.GetAsync<byte[]>(cacheKey);
            if (cached != null)
            {
                // Still need the name; use a lightweight cache key for it
                string nameCacheKey = $"pdf:bracket:name:{tournamentId}";
                var cachedName = await this.cacheService.GetAsync<string>(nameCacheKey) ?? string.Empty;
                return (cached, cachedName);
            }

            var structure = await this.bracketService.GetTournamentStructure(tournamentId);

            var document = Document.Create(doc =>
            {
                foreach (var stage in structure.Stages)
                {
                    if (stage.Rounds is { Count: > 0 })
                    {
                        // The standard bracket page assumes a binary tree (each round halves the
                        // previous one). The Losers Bracket alternates major / minor rounds with
                        // equal match counts, so it needs a flat column-per-round layout instead.
                        if (stage.Type == StageType.DoubleEliminationLosersBracket)
                            CreateLosersBracketPage(doc, structure, stage);
                        else
                            CreateBracketPage(doc, structure, stage);
                    }

                    if (stage.Groups is { Count: > 0 })
                        CreateGroupPage(doc, structure, stage);
                }
            });

            var pdf = document.GeneratePdf();
            await this.cacheService.SetAsync(cacheKey, pdf, TimeSpan.FromMinutes(5));
            await this.cacheService.SetAsync($"pdf:bracket:name:{tournamentId}", structure.Name, TimeSpan.FromMinutes(5));
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

        // ── Losers Bracket page (flat column-per-round layout) ─────────

        private static void CreateLosersBracketPage(
            IDocumentContainer doc,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage)
        {
            var rounds = stage.Rounds!;
            int maxMatches = rounds.Max(r => r.Matches.Count);

            float bracketW = rounds.Count * ColW - RoundGap;
            float bracketH = maxMatches * (MatchBoxH + BaseGapY) - BaseGapY;
            float pageW = Math.Max(bracketW + Pad * 2, 842);
            float pageH = Math.Max(HeaderH + RoundLblH + bracketH + FooterH + 30, 420);

            string svg = BuildLosersBracketSvg(pageW, pageH, structure, stage, rounds);

            doc.Page(page =>
            {
                page.Size(pageW, pageH);
                page.Margin(0);
                page.Content().Svg(SvgImage.FromText(svg));
            });
        }

        private static string BuildLosersBracketSvg(
            float w, float h,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage,
            List<BracketRoundDto> rounds)
        {
            var sb = new StringBuilder(65536);

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{P(w)}\" height=\"{P(h)}\" viewBox=\"0 0 {P(w)} {P(h)}\">");

            // Clip paths for match boxes
            sb.Append("<defs>");
            int idx = 0;
            foreach (var round in rounds)
                foreach (var _ in round.Matches)
                {
                    sb.Append($"<clipPath id=\"lcp{idx}\"><rect x=\"0\" y=\"0\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\"/></clipPath>");
                    idx++;
                }
            sb.Append("</defs>");

            // ── Editorial header ───────────────────────────────────
            // Same layout as the WB page so the two pages of a DE bracket read as one set.
            sb.Append($"<text x=\"{P(Pad)}\" y=\"20\" fill=\"{CGroupName}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\" letter-spacing=\"0.6\">GAMEHUBZ</text>");
            sb.Append($"<rect x=\"{P(Pad - 12)}\" y=\"13\" width=\"6\" height=\"6\" fill=\"{CAccent}\" transform=\"rotate(45 {P(Pad - 9)} 16)\"/>");
            sb.Append($"<text x=\"{P(w / 2)}\" y=\"20\" fill=\"{CMuted}\" font-size=\"8\" font-family=\"{Font}\" text-anchor=\"middle\" letter-spacing=\"0.5\">{Esc(FormatTopMeta(structure))}</text>");

            var (statusTxt2, statusCol2) = FormatStatusBadge(structure.Status);
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"20\" fill=\"{statusCol2}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\" letter-spacing=\"0.8\">{Esc(statusTxt2)}</text>");

            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"32\" x2=\"{P(w - Pad)}\" y2=\"32\" stroke=\"{CCardBrd}\" stroke-width=\"0.5\"/>");

            // Title block: "LOSERS BRACKET" caption distinguishes this page from the WB page;
            // big title shows the stage name as the editor entered it.
            sb.Append($"<text x=\"{P(Pad)}\" y=\"58\" fill=\"{CMuted}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" letter-spacing=\"1.2\">LOSERS BRACKET</text>");
            sb.Append($"<text x=\"{P(Pad)}\" y=\"90\" fill=\"{CGroupName}\" font-size=\"24\" font-weight=\"bold\" font-family=\"{Font}\">{Esc(stage.Name)}</text>");

            // Right side: format + first-round entry count (a useful sanity check for LB size).
            int firstRoundEntries = rounds[0].Matches.Count * 2;
            string formatLine = $"{SplitPascal(structure.Format.ToString()).ToUpperInvariant()} · {firstRoundEntries}";
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"58\" fill=\"{CMuted}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\" letter-spacing=\"1.2\">FORMAT</text>");
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"86\" fill=\"{CGroupName}\" font-size=\"13\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\">{Esc(formatLine)}</text>");

            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"{P(HeaderH - 6)}\" x2=\"{P(w - Pad)}\" y2=\"{P(HeaderH - 6)}\" stroke=\"{CCardBrd}\" stroke-width=\"0.5\"/>");

            // ── Round labels ───────────────────────────────────────
            // LB rounds get descriptive names ("LB FINAL", "LB SEMIFINAL", …) anchored to the
            // MAX persisted round number rather than rounds.Count so a DE bye cascade that
            // collapses early LB rounds out of the structure doesn't shift every later round's
            // label upward. Style matches the editorial WB labels (plain uppercase tracking).
            float rlY = HeaderH + 6;
            int maxRound = rounds.Max(r => r.RoundNumber);
            for (int i = 0; i < rounds.Count; i++)
            {
                float cx = Pad + i * ColW + MatchBoxW / 2;
                int rn = rounds[i].RoundNumber;
                string lbl = rn == maxRound
                    ? "LB FINAL"
                    : rn == maxRound - 1
                        ? "LB SEMIFINAL"
                        : $"LB ROUND {rn}";
                sb.Append($"<text x=\"{P(cx)}\" y=\"{P(rlY + 14)}\" fill=\"{CMuted}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"middle\" letter-spacing=\"1.2\">{Esc(lbl)}</text>");
            }

            // Positions: each column independently centers its matches in the column area.
            float bTop = rlY + RoundLblH + 4;
            int maxMatches = rounds.Max(r => r.Matches.Count);
            float colH = maxMatches * (MatchBoxH + BaseGapY) - BaseGapY;

            var positions = new List<List<float>>(rounds.Count);
            foreach (var round in rounds)
            {
                int n = round.Matches.Count;
                float thisColH = n * (MatchBoxH + BaseGapY) - BaseGapY;
                float colTop = bTop + (colH - thisColH) / 2;
                var list = new List<float>(n);
                for (int i = 0; i < n; i++)
                    list.Add(colTop + i * (MatchBoxH + BaseGapY) + MatchBoxH / 2);
                positions.Add(list);
            }

            // Connectors: a same-count step (minor → major, or LB R1 → LB R2) draws straight
            // lines between matching cards. A halving step (major → minor) draws a Y-junction
            // pairing successive cards into the next round. WB-loser drop-ins enter from outside
            // the LB page and are intentionally not drawn — they're hinted by a small "← WB" tag.
            sb.Append($"<g stroke=\"{CConn}\" stroke-width=\"{P(ConnStroke)}\" stroke-linecap=\"round\" fill=\"none\">");
            for (int r = 0; r < rounds.Count - 1; r++)
            {
                float srcR = Pad + r * ColW + MatchBoxW;
                float midX = srcR + RoundGap / 2;
                float dstL = Pad + (r + 1) * ColW;

                int curCount = rounds[r].Matches.Count;
                int nextCount = rounds[r + 1].Matches.Count;

                if (nextCount == curCount)
                {
                    // 1:1 mapping — each match feeds the same-index match in the next column.
                    for (int j = 0; j < curCount; j++)
                    {
                        float y = positions[r][j];
                        float yD = positions[r + 1][j];
                        sb.Append($"<line x1=\"{P(srcR)}\" y1=\"{P(y)}\" x2=\"{P(midX)}\" y2=\"{P(y)}\"/>");
                        sb.Append($"<line x1=\"{P(midX)}\" y1=\"{P(y)}\" x2=\"{P(midX)}\" y2=\"{P(yD)}\"/>");
                        sb.Append($"<line x1=\"{P(midX)}\" y1=\"{P(yD)}\" x2=\"{P(dstL)}\" y2=\"{P(yD)}\"/>");
                    }
                }
                else if (curCount == 2 * nextCount)
                {
                    // 2:1 pairing — successive matches feed one next-round match.
                    for (int j = 0; j < nextCount; j++)
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
                // Any other ratio is unexpected for an LB shape; leave it unconnected rather
                // than guess wrong.
            }
            sb.Append("</g>");

            // Match boxes
            int ci = 0;
            for (int r = 0; r < rounds.Count; r++)
            {
                float x = Pad + r * ColW;
                for (int m = 0; m < rounds[r].Matches.Count && m < positions[r].Count; m++)
                {
                    float cy = positions[r][m];
                    float by = cy - MatchBoxH / 2;
                    AppendLosersMatchBox(sb, x, by, rounds[r].Matches[m], structure.IsTeamTournament, ci++);
                }
            }

            // Footer
            float fY = h - FooterH;
            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"{P(fY)}\" x2=\"{P(w - Pad)}\" y2=\"{P(fY)}\" stroke=\"#E2E8F0\"/>");
            string ts = $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  •  GameHubz";
            sb.Append($"<text x=\"{P(Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"10\" font-family=\"{Font}\">{Esc(ts)}</text>");
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"10\" font-family=\"{Font}\" text-anchor=\"end\">gamehubz.com</text>");

            sb.Append("</svg>");
            return sb.ToString();
        }

        // Mirror of AppendMatchBox but using its own clip-path id namespace so a single PDF
        // with both the WB and the LB pages doesn't end up with two clipPaths sharing an id.
        private static void AppendLosersMatchBox(
            StringBuilder sb, float x, float y,
            MatchStructureDto match, bool isTeam, int clipIdx)
        {
            string home = Trunc(GetName(match.Home, isTeam));
            string away = Trunc(GetName(match.Away, isTeam));
            bool hw = match.Home?.IsWinner == true;
            bool aw = match.Away?.IsWinner == true;
            bool done = match.Status == MatchStatus.Completed;
            bool hasWinner = hw || aw;

            sb.Append("<g>");

            sb.Append($"<rect x=\"{P(x + 2)}\" y=\"{P(y + 2)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"#00000012\"/>");
            sb.Append($"<rect x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"{CBoxBg}\"/>");

            sb.Append($"<g transform=\"translate({P(x)},{P(y)})\" clip-path=\"url(#lcp{clipIdx})\">");
            if (hw)
                sb.Append($"<rect width=\"{P(MatchBoxW)}\" height=\"{P(RowH)}\" fill=\"{CWinBg}\"/>");
            if (aw)
                sb.Append($"<rect y=\"{P(RowH)}\" width=\"{P(MatchBoxW)}\" height=\"{P(RowH)}\" fill=\"{CWinBg}\"/>");
            if (hasWinner)
                sb.Append($"<rect width=\"{P(AccentW)}\" height=\"{P(MatchBoxH)}\" fill=\"{CWinBar}\"/>");
            sb.Append("</g>");

            sb.Append($"<rect x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"none\" stroke=\"{CBoxBrd}\"/>");
            sb.Append($"<line x1=\"{P(x + 8)}\" y1=\"{P(y + RowH)}\" x2=\"{P(x + MatchBoxW - 8)}\" y2=\"{P(y + RowH)}\" stroke=\"{CSep}\"/>");

            AppendRow(sb, x, y, home, match.Home?.Score, match.Home?.Seed, hw, match.Home == null, done);
            AppendRow(sb, x, y + RowH, away, match.Away?.Score, match.Away?.Seed, aw, match.Away == null, done);

            sb.Append("</g>");
        }

        // ── SVG builder ────────────────────────────────────────────────

        private static string BuildSvg(
            float w, float h,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage,
            List<BracketRoundDto> rounds)
        {
            var sb = new StringBuilder(65536);

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{P(w)}\" height=\"{P(h)}\" viewBox=\"0 0 {P(w)} {P(h)}\">");

            // Defs
            sb.Append("<defs>");

            // Clip paths for match boxes
            int idx = 0;
            foreach (var round in rounds)
                foreach (var _ in round.Matches)
                {
                    sb.Append($"<clipPath id=\"cp{idx}\"><rect x=\"0\" y=\"0\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\"/></clipPath>");
                    idx++;
                }
            sb.Append("</defs>");

            // ── Editorial header ───────────────────────────────────
            // Top metadata strip: wordmark left, tournament info middle, status right.
            sb.Append($"<text x=\"{P(Pad)}\" y=\"20\" fill=\"{CGroupName}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\" letter-spacing=\"0.6\">GAMEHUBZ</text>");
            sb.Append($"<rect x=\"{P(Pad - 12)}\" y=\"13\" width=\"6\" height=\"6\" fill=\"{CAccent}\" transform=\"rotate(45 {P(Pad - 9)} 16)\"/>");
            sb.Append($"<text x=\"{P(w / 2)}\" y=\"20\" fill=\"{CMuted}\" font-size=\"8\" font-family=\"{Font}\" text-anchor=\"middle\" letter-spacing=\"0.5\">{Esc(FormatTopMeta(structure))}</text>");

            var (statusTxt2, statusCol2) = FormatStatusBadge(structure.Status);
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"20\" fill=\"{statusCol2}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\" letter-spacing=\"0.8\">{Esc(statusTxt2)}</text>");

            // Hairline divider under the metadata strip
            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"32\" x2=\"{P(w - Pad)}\" y2=\"32\" stroke=\"{CCardBrd}\" stroke-width=\"0.5\"/>");

            // Title block: tiny "BRACKET" caption above the big stage title; on the right,
            // a mirroring "FORMAT" caption above the format / participant count.
            sb.Append($"<text x=\"{P(Pad)}\" y=\"58\" fill=\"{CMuted}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" letter-spacing=\"1.2\">BRACKET</text>");
            sb.Append($"<text x=\"{P(Pad)}\" y=\"90\" fill=\"{CGroupName}\" font-size=\"24\" font-weight=\"bold\" font-family=\"{Font}\">{Esc(stage.Name)}</text>");

            int totalParticipants = rounds[0].Matches.Count * 2;
            string formatLine = $"{SplitPascal(structure.Format.ToString()).ToUpperInvariant()} · {totalParticipants}";
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"58\" fill=\"{CMuted}\" font-size=\"8\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\" letter-spacing=\"1.2\">FORMAT</text>");
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"86\" fill=\"{CGroupName}\" font-size=\"13\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"end\">{Esc(formatLine)}</text>");

            // Hairline divider under the title block, marking the start of bracket content
            sb.Append($"<line x1=\"{P(Pad)}\" y1=\"{P(HeaderH - 6)}\" x2=\"{P(w - Pad)}\" y2=\"{P(HeaderH - 6)}\" stroke=\"{CCardBrd}\" stroke-width=\"0.5\"/>");

            // ── Round labels ───────────────────────────────────────
            // Plain uppercase tracking instead of the old dark pill — matches the editorial header.
            // Use ResolveRoundLabel so the terminal rounds (Final, Semifinal, Grand Final, …) read
            // by name instead of as a generic "ROUND N" pulled from the DTO.
            float rlY = HeaderH + 6;
            for (int i = 0; i < rounds.Count; i++)
            {
                float cx = Pad + i * ColW + MatchBoxW / 2;
                string lbl = ResolveRoundLabel(rounds[i]).ToUpperInvariant();
                sb.Append($"<text x=\"{P(cx)}\" y=\"{P(rlY + 14)}\" fill=\"{CMuted}\" font-size=\"9\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"middle\" letter-spacing=\"1.2\">{Esc(lbl)}</text>");
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
            sb.Append($"<text x=\"{P(Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"10\" font-family=\"{Font}\">{Esc(ts)}</text>");
            sb.Append($"<text x=\"{P(w - Pad)}\" y=\"{P(fY + 16)}\" fill=\"{CMuted}\" font-size=\"10\" font-family=\"{Font}\" text-anchor=\"end\">gamehubz.com</text>");

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

            sb.Append("<g>");

            // Cheap offset shadow (no filter/blur needed)
            sb.Append($"<rect x=\"{P(x + 2)}\" y=\"{P(y + 2)}\" width=\"{P(MatchBoxW)}\" height=\"{P(MatchBoxH)}\" rx=\"{P(BoxR)}\" fill=\"#00000012\"/>");

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
            float ty = ry + RowH * 0.62f;

            // Seed
            if (seed.HasValue)
            {
                sb.Append($"<text x=\"{P(tx)}\" y=\"{P(ty)}\" fill=\"{CSeed}\" font-size=\"11\" font-family=\"{Font}\">{seed.Value}</text>");
                tx += 16;
            }

            // Name
            string col = tbd ? CTbd : win ? CWinTxt : CTxt;
            string bold = win ? " font-weight=\"bold\"" : "";
            sb.Append($"<text x=\"{P(tx)}\" y=\"{P(ty)}\" fill=\"{col}\" font-size=\"14\"{bold} font-family=\"{Font}\">{Esc(name)}</text>");

            // Score badge
            if (done && score != null)
            {
                string bg = win ? CScoreW : CScoreL;
                float sx = bx + MatchBoxW - ScoreBadgeW - 7;
                float sy = ry + (RowH - ScoreBadgeH) / 2;
                float badgeTextY = sy + ScoreBadgeH * 0.72f;
                sb.Append($"<rect x=\"{P(sx)}\" y=\"{P(sy)}\" width=\"{P(ScoreBadgeW)}\" height=\"{P(ScoreBadgeH)}\" rx=\"3\" fill=\"{bg}\"/>");
                sb.Append($"<text x=\"{P(sx + ScoreBadgeW / 2)}\" y=\"{P(badgeTextY)}\" fill=\"{CWhite}\" font-size=\"13\" font-weight=\"bold\" font-family=\"{Font}\" text-anchor=\"middle\">{score.Value}</text>");
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
            var groups = stage.Groups!.OrderBy(g => g.Name).ToList();
            if (groups.Count == 0) return;

            int qualifiersCount = structure.QualifiersPerGroup ?? 1;
            int maxSize = groups.Max(g => g.Standings.Count);

            // Fixed 3-column grid for the common case (≤6-player groups), 2 cols for
            // medium-size groups, 1 col for the very large ones. The page is a bit wider
            // than A4 landscape so each card has enough horizontal room for long names
            // without truncation.
            int columns = maxSize <= 6 ? 3 : maxSize <= 12 ? 2 : 1;

            // Rows per page sized so a single batch fits cleanly in the page body — this
            // lets us emit one doc.Page per batch and have each page's header show only
            // the groups on that page (e.g. "A — I" on page 1, "J — P" on page 2).
            int rowsPerPage = maxSize <= 6 ? 3 : maxSize <= 12 ? 2 : 1;
            int groupsPerPage = columns * rowsPerPage;

            for (int start = 0; start < groups.Count; start += groupsPerPage)
            {
                var pageGroups = groups.Skip(start).Take(groupsPerPage).ToList();
                RenderGroupDocPage(doc, structure, stage, pageGroups, qualifiersCount, columns);
            }
        }

        private static void RenderGroupDocPage(
            IDocumentContainer doc,
            TournamentStructureDto structure,
            TournamentStageStructureDto stage,
            List<GroupDto> groups,
            int qualifiersCount,
            int columns)
        {
            // Wider than A4 landscape (842pt) but narrower than the bracket page — gives
            // each of the 3 columns ~370pt, plenty for long usernames / team names.
            const float pageW = 1200;
            const float pageH = 700;

            doc.Page(page =>
            {
                page.Size(pageW, pageH);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Font));

                page.Header().Column(h =>
                {
                    // Top metadata strip: wordmark on the left, format + date in the middle,
                    // status on the right. Small caps + letter spacing keeps it editorial.
                    h.Item().PaddingBottom(8).Row(r =>
                    {
                        r.RelativeItem().AlignMiddle().Text("GAMEHUBZ")
                            .FontSize(9).Bold().FontColor(CGroupName).LetterSpacing(0.08f);

                        r.RelativeItem(2).AlignCenter().AlignMiddle().Text(t =>
                        {
                            t.Span(FormatTopMeta(structure))
                                .FontSize(8).FontColor(CMuted).LetterSpacing(0.06f);
                        });

                        r.RelativeItem().AlignRight().AlignMiddle().Text(t =>
                        {
                            var (txt, col) = FormatStatusBadge(structure.Status);
                            t.Span(txt).FontSize(8).Bold().FontColor(col).LetterSpacing(0.08f);
                        });
                    });

                    h.Item().LineHorizontal(0.5f).LineColor(CCardBrd);

                    // Title block: tiny uppercase section label above the big stage title,
                    // with right-side context (group range) mirrored on the other end.
                    h.Item().PaddingTop(14).PaddingBottom(14).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("STANDINGS")
                                .FontSize(8).Bold().FontColor(CMuted).LetterSpacing(0.12f);
                            c.Item().PaddingTop(3).Text(stage.Name)
                                .FontSize(22).Bold().FontColor(CGroupName);
                        });

                        r.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("GROUPS")
                                .FontSize(8).Bold().FontColor(CMuted).LetterSpacing(0.12f);
                            c.Item().PaddingTop(3).AlignRight().Text(FormatGroupRange(groups))
                                .FontSize(13).Bold().FontColor(CGroupName);
                        });
                    });

                    h.Item().LineHorizontal(0.5f).LineColor(CCardBrd);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(12);

                    // Each row of `columns` group cards is kept whole with ShowEntire so a card
                    // never breaks mid-table across pages.
                    for (int i = 0; i < groups.Count; i += columns)
                    {
                        var rowGroups = groups.Skip(i).Take(columns).ToList();
                        col.Item().ShowEntire().Row(r =>
                        {
                            r.Spacing(12);
                            foreach (var g in rowGroups)
                                r.RelativeItem().Element(e => RenderGroupCard(e, g, qualifiersCount));

                            // Pad partial rows with empty cells so cards stay aligned with the grid.
                            for (int j = rowGroups.Count; j < columns; j++)
                                r.RelativeItem();
                        });
                    }
                });

                page.Footer()
                    .BorderTop(1).BorderColor(Colors.Grey.Lighten2)
                    .PaddingTop(5)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  \u2022  GameHubz")
                            .FontSize(7).FontColor(Colors.Grey.Medium);

                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.Span("Page ").FontSize(7).FontColor(Colors.Grey.Medium);
                            t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                            t.Span(" of ").FontSize(7).FontColor(Colors.Grey.Medium);
                            t.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });
            });
        }

        // Clean group card: white background, thin border, plain dark title, compact uppercase
        // headers and a single subtle highlight for qualifier rows. Name column is relative so
        // long usernames / team names absorb leftover width while numeric columns stay narrow.
        private static void RenderGroupCard(IContainer container, GroupDto group, int qualifiersCount)
        {
            container
                .Background(CCardBg)
                .Border(0.75f).BorderColor(CCardBrd)
                .Padding(12)
                .Column(card =>
                {
                    card.Item().PaddingBottom(8)
                        .Text(group.Name)
                        .FontSize(13).Bold().FontColor(CGroupName);

                    card.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(16);   // #
                            c.RelativeColumn();     // Name (flexes — absorbs leftover width)
                            c.ConstantColumn(26);   // Pts
                            c.ConstantColumn(18);   // W
                            c.ConstantColumn(18);   // D
                            c.ConstantColumn(18);   // L
                            c.ConstantColumn(22);   // GF
                            c.ConstantColumn(22);   // GA
                            c.ConstantColumn(26);   // GD
                        });

                        table.Header(h =>
                        {
                            var hs = TextStyle.Default.FontSize(7).FontColor(CTblHdr).LetterSpacing(0.04f);

                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).PaddingHorizontal(2).Text("#").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).PaddingHorizontal(2).Text("PLAYER").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("PTS").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("W").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("D").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("L").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("GF").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("GA").Style(hs);
                            h.Cell().BorderBottom(0.5f).BorderColor(CCardBrd).PaddingVertical(4).AlignCenter().Text("GD").Style(hs);
                        });

                        var rowBase = TextStyle.Default.FontSize(9).FontColor(CRowTxt);
                        var rowPos = rowBase.FontColor(CRowMuted);
                        var rowPts = rowBase.Bold();

                        foreach (var s in group.Standings)
                        {
                            bool isQualifier = s.Position <= qualifiersCount;
                            string bg = isQualifier ? CQualifierBg : CCardBg;
                            var nameStyle = isQualifier ? rowBase.Bold() : rowBase;

                            string gdText = s.GoalDifference > 0
                                ? $"+{s.GoalDifference}"
                                : s.GoalDifference.ToString();
                            string gdColor = s.GoalDifference > 0
                                ? CGdPos
                                : s.GoalDifference < 0 ? CGdNeg : CGdNeut;
                            var gdStyle = rowBase.FontColor(gdColor).Bold();

                            table.Cell().Background(bg).PaddingVertical(5).PaddingHorizontal(2).Text(s.Position.ToString()).Style(rowPos);
                            table.Cell().Background(bg).PaddingVertical(5).PaddingHorizontal(2)
                                .Text(s.Name).Style(nameStyle).ClampLines(1, "…");
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.Points.ToString()).Style(rowPts);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.Wins.ToString()).Style(rowBase);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.Draws.ToString()).Style(rowBase);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.Losses.ToString()).Style(rowBase);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.GoalsFor.ToString()).Style(rowBase);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(s.GoalsAgainst.ToString()).Style(rowBase);
                            table.Cell().Background(bg).PaddingVertical(5).AlignCenter().Text(gdText).Style(gdStyle);
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

        private static string P(float v) => v.ToString("0.###", Inv);

        // ── Editorial header helpers ───────────────────────────────────

        // Top metadata line: tournament name · format · mode · timestamp, dot-separated
        // with small caps so it reads as a single subtle ribbon.
        private static string FormatTopMeta(TournamentStructureDto structure)
        {
            var bits = new[]
            {
                structure.Name.ToUpperInvariant(),
                SplitPascal(structure.Format.ToString()).ToUpperInvariant(),
                structure.IsTeamTournament ? "TEAMS" : "SOLO",
                DateTime.UtcNow.ToString("yyyy.MM.dd · HH:mm 'UTC'", Inv),
            };
            return string.Join("   ·   ", bits);
        }

        // Inserts spaces before each capital letter inside a PascalCase enum name so
        // "GroupStageWithKnockout" reads as "Group Stage With Knockout".
        private static string SplitPascal(string s)
            => System.Text.RegularExpressions.Regex.Replace(s, "(?<!^)([A-Z])", " $1");

        // Status badge text + color. Matches the small uppercase status pill in the reference.
        private static (string Text, string Color) FormatStatusBadge(TournamentStatus status) => status switch
        {
            TournamentStatus.InProgress => ("IN PROGRESS", CAccent),
            TournamentStatus.Completed => ("COMPLETED", CGroupName),
            _ => (status.ToString().ToUpperInvariant(), CMuted),
        };

        // Group range label — "A — P" given "Group A" … "Group P", or just the single
        // group name if there's only one.
        private static string FormatGroupRange(List<GroupDto> groups)
        {
            if (groups.Count == 0) return "";
            string first = StripGroupPrefix(groups.First().Name);
            string last = StripGroupPrefix(groups.Last().Name);
            return first == last ? first : $"{first} — {last}";
        }

        private static string StripGroupPrefix(string name)
            => name.StartsWith("Group ", StringComparison.OrdinalIgnoreCase)
                ? name[6..].Trim()
                : name;

        // Prefers the most descriptive label available for a bracket round. The DTO's default
        // name ("Round N") is fine for the early WB rounds but obscures the meaning of the
        // late-stage rounds (Quarter / Semi / Final / Grand Final). When the round contains a
        // match flagged with a recognised MatchStage, we surface that stage's name.
        private static string ResolveRoundLabel(BracketRoundDto round)
        {
            if (round.Matches.Count == 0) return round.Name;

            var firstStage = round.Matches[0].Stage;
            return firstStage switch
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
    }
}