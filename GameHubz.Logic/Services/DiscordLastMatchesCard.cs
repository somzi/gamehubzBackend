using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
// Both SixLabors and QuestPDF define Image/Size — alias the imaging ones used in TryMakeCircle.
using ISImage = SixLabors.ImageSharp.Image;
using ISSize = SixLabors.ImageSharp.Size;

namespace GameHubz.Logic.Services
{
    /// <summary>Display data for the /lastmatches Discord card. Everything is pre-resolved
    /// so the renderer stays pure (no DB / catalog lookups inside the layout).</summary>
    public class LastMatchesCardData
    {
        public string Name { get; set; } = "";

        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }

        public List<LastMatchesCardRow> Rows { get; set; } = new();

        /// <summary>Render moment, UTC — shown in the footer.</summary>
        public DateTime GeneratedAtUtc { get; set; }
    }

    public class LastMatchesCardRow
    {
        public string Opponent { get; set; } = "";
        public string Tournament { get; set; } = "";
        public string Hub { get; set; } = "";

        /// <summary>"W" / "L" / "D".</summary>
        public string Outcome { get; set; } = "L";

        public int? MyScore { get; set; }
        public int? OpponentScore { get; set; }

        /// <summary>UTC. Null → no date line.</summary>
        public DateTime? PlayedAt { get; set; }

        public byte[]? Avatar { get; set; }
    }

    /// <summary>
    /// Renders the /lastmatches card to a PNG via QuestPDF — same panel style as the other Discord
    /// cards. Header with a W/L/D tally, then one full-width row per completed match: outcome
    /// badge, opponent avatar + name + context, and a bold score chip with the date underneath.
    /// Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordLastMatchesCard
    {
        /// <summary>Rows rendered on the card; extras aren't listed (paged via the mobile app).</summary>
        public const int MaxRows = 8;

        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look.
        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Win = "#34D399";
        private const string Loss = "#F87171";
        private const string Draw = "#FBBF24";

        public static byte[] Render(LastMatchesCardData d)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.ContinuousSize(500);
                    page.PageColor(Bg);
                    page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(12).FontColor(TextHi));

                    page.Content().Padding(12).Column(col =>
                    {
                        col.Item()
                            .CornerRadius(22)
                            .Background(Panel)
                            .Border(1.5f).BorderColor(Stroke)
                            .Padding(20)
                            .Column(panel =>
                            {
                                panel.Spacing(16);

                                Header(panel, d);

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                if (d.Rows.Count == 0)
                                {
                                    EmptyState(panel);
                                }
                                else
                                {
                                    panel.Item().Row(r =>
                                    {
                                        r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(HistoryIcon(Accent));
                                        r.AutoItem().AlignMiddle().PaddingLeft(7)
                                            .Text("RECENT RESULTS").FontSize(10).Bold().FontColor(TextLo).LetterSpacing(0.15f);
                                    });

                                    panel.Item().Column(list =>
                                    {
                                        list.Spacing(10);
                                        foreach (var row in d.Rows.Take(MaxRows))
                                            MatchRow(list, row);
                                    });
                                }

                                Footer(panel, d);
                            });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void Header(ColumnDescriptor panel, LastMatchesCardData d)
        {
            panel.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Text(d.Name).FontSize(22).Bold().FontColor(TextHi).ClampLines(1, "…");
                    c.Item().PaddingTop(4).Text("LAST MATCHES").FontSize(9.5f).Bold().FontColor(Accent).LetterSpacing(0.18f);
                });

                row.AutoItem().AlignMiddle().Row(tally =>
                {
                    TallyPill(tally, d.Wins.ToString(), "W", Win);
                    tally.ConstantItem(6);
                    TallyPill(tally, d.Draws.ToString(), "D", Draw);
                    tally.ConstantItem(6);
                    TallyPill(tally, d.Losses.ToString(), "L", Loss);
                });
            });
        }

        private static void TallyPill(RowDescriptor tally, string value, string label, string color)
        {
            tally.AutoItem().CornerRadius(10).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(6).PaddingHorizontal(10).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Text(value).FontSize(14).Bold().FontColor(color);
                    r.AutoItem().AlignMiddle().PaddingLeft(4).Text(label).FontSize(8).Bold().FontColor(TextLo);
                });
        }

        private static void MatchRow(ColumnDescriptor list, LastMatchesCardRow m)
        {
            byte[]? avatar = TryMakeCircle(m.Avatar, 120);
            string color = m.Outcome switch
            {
                "W" => Win,
                "D" => Draw,
                _ => Loss,
            };

            list.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Row(row =>
            {
                // Outcome badge — colored circle with W/L/D letter.
                row.ConstantItem(38).Height(38).CornerRadius(19).Background(Bg).Border(1.5f).BorderColor(color)
                    .AlignMiddle().AlignCenter().Text(m.Outcome).FontSize(15).Bold().FontColor(color);

                // Opponent avatar (fallback = initial).
                if (avatar != null)
                    row.ConstantItem(38).PaddingLeft(10).Height(38).Image(avatar).FitArea();
                else
                    row.ConstantItem(38).PaddingLeft(10).Column(c => c.Item().Height(38).CornerRadius(19).Background(Bg).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(m.Opponent)).FontSize(15).Bold().FontColor(Accent));

                // Opponent + tournament context.
                row.RelativeItem().PaddingLeft(11).AlignMiddle().Column(c =>
                {
                    c.Item().Row(nr =>
                    {
                        nr.AutoItem().AlignBottom().PaddingBottom(2).PaddingRight(6)
                            .Text("vs").FontSize(11).Bold().FontColor(Accent);
                        nr.RelativeItem().Text(m.Opponent).FontSize(15).Bold().FontColor(TextHi).ClampLines(1, "…");
                    });
                    c.Item().PaddingTop(3).Text($"{m.Tournament} · {m.Hub}").FontSize(9.5f).FontColor(TextLo).ClampLines(1, "…");
                });

                // Score chip + played-at date.
                row.ConstantItem(115).AlignMiddle().Column(c =>
                {
                    c.Spacing(5);

                    c.Item().AlignRight().CornerRadius(9).Background(Bg).Border(1).BorderColor(Stroke)
                        .PaddingVertical(5).PaddingHorizontal(10).Row(r =>
                        {
                            r.AutoItem().AlignMiddle().Text(ScoreOrDash(m.MyScore)).FontSize(15).Bold().FontColor(color);
                            r.AutoItem().AlignMiddle().PaddingHorizontal(5).Text("–").FontSize(13).FontColor(TextLo);
                            r.AutoItem().AlignMiddle().Text(ScoreOrDash(m.OpponentScore)).FontSize(15).Bold().FontColor(TextLo);
                        });

                    if (m.PlayedAt.HasValue)
                        c.Item().AlignRight().Text(FormatUtc(m.PlayedAt.Value, "dd MMM yyyy")).FontSize(8.5f).FontColor(TextLo);
                });
            });
        }

        private static void EmptyState(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(34).Column(c =>
                {
                    c.Item().AlignCenter().Width(34).Height(34).Svg(HistoryIcon(TextLo));
                    c.Item().PaddingTop(12).AlignCenter().Text("No completed matches yet").FontSize(15).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter().Text("Play your first match and your history will show here.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Footer(ColumnDescriptor panel, LastMatchesCardData d)
        {
            panel.Item().CornerRadius(12).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(10).PaddingHorizontal(14).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(TrophyIcon(Accent));
                    r.AutoItem().AlignMiddle().PaddingLeft(7).Text("GameHubz").FontSize(10.5f).Bold().FontColor(TextHi);
                    r.RelativeItem().AlignMiddle().AlignRight()
                        .Text(FormatUtc(d.GeneratedAtUtc, "dd MMM yyyy · HH:mm") + " UTC").FontSize(9).FontColor(TextLo);
                });
        }

        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Initial(string name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

        private static string ScoreOrDash(int? value) => value?.ToString() ?? "–";

        // ── SVG line icons ──

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string HistoryIcon(string color)
            => Svg("<path d=\"M3 12a9 9 0 1 0 3-6.7L3 8\"/><path d=\"M3 3v5h5\"/><path d=\"M12 7v5l3 2\"/>", color);

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

        // Circular-crops the avatar with pure ImageSharp — matches the pattern used by the other
        // Discord cards. Any failure (bad bytes, unreachable image) → no avatar.
        private static byte[]? TryMakeCircle(byte[]? source, int size)
        {
            if (source == null || source.Length == 0)
                return null;

            try
            {
                using var image = ISImage.Load<Rgba32>(source);
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new ISSize(size, size), Mode = ResizeMode.Crop }));

                float r = size / 2f;
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> rowSpan = accessor.GetRowSpan(y);
                        for (int x = 0; x < rowSpan.Length; x++)
                        {
                            float dx = x + 0.5f - r;
                            float dy = y + 0.5f - r;
                            if (dx * dx + dy * dy > r * r)
                                rowSpan[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                });

                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
