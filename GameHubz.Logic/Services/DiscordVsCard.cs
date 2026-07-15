using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ISImage = SixLabors.ImageSharp.Image;
using ISSize = SixLabors.ImageSharp.Size;

namespace GameHubz.Logic.Services
{
    /// <summary>Display data for the /vs Discord card. All values from the caller's perspective
    /// (MyWins = the caller's wins over the opponent). Everything is pre-resolved so the renderer
    /// stays pure (no DB / catalog lookups inside the layout).</summary>
    public class VsCardData
    {
        public string PlayerName { get; set; } = "";
        public byte[]? PlayerAvatar { get; set; }

        public string OpponentName { get; set; } = "";
        public byte[]? OpponentAvatar { get; set; }

        public int TotalMatches { get; set; }
        public int MyWins { get; set; }
        public int OpponentWins { get; set; }
        public int Draws { get; set; }

        /// <summary>"W" / "L" / "D" from the caller's perspective, null when no matches.</summary>
        public string? LastOutcome { get; set; }

        public int? LastMyScore { get; set; }
        public int? LastOpponentScore { get; set; }
        public DateTime? LastMatchTime { get; set; }
        public string? LastTournamentName { get; set; }
        public string? LastHubName { get; set; }

        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders the /vs head-to-head card to a PNG via QuestPDF — same compact panel style as the
    /// other Discord cards. Face-off with both circular avatars around a big "W – D – L" tally,
    /// then a "Last meeting" block with the final score and tournament, or a "First meeting" empty
    /// state when the two have never played. Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordVsCard
    {
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

        public static byte[] Render(VsCardData d)
        {
            byte[]? playerAvatar = TryMakeCircle(d.PlayerAvatar, 200);
            byte[]? opponentAvatar = TryMakeCircle(d.OpponentAvatar, 200);

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

                                panel.Item().Row(r =>
                                {
                                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(SwordsIcon(Accent));
                                    r.AutoItem().AlignMiddle().PaddingLeft(7)
                                        .Text("HEAD-TO-HEAD").FontSize(10).Bold().FontColor(Accent).LetterSpacing(0.15f);
                                    r.RelativeItem().AlignMiddle().AlignRight()
                                        .Text($"{d.TotalMatches} {(d.TotalMatches == 1 ? "MEETING" : "MEETINGS")}")
                                        .FontSize(9).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                                });

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                FaceOff(panel, d, playerAvatar, opponentAvatar);

                                if (d.TotalMatches > 0)
                                    LastMeeting(panel, d);
                                else
                                    FirstMeeting(panel);

                                Footer(panel, d);
                            });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void FaceOff(ColumnDescriptor panel, VsCardData d, byte[]? playerAvatar, byte[]? opponentAvatar)
        {
            panel.Item().PaddingTop(4).Row(row =>
            {
                Side(row, d.PlayerName, playerAvatar);

                // Central W – D – L tally, sized so the two W/L counts read as the story.
                row.ConstantItem(150).AlignMiddle().Column(c =>
                {
                    c.Item().Row(t =>
                    {
                        t.RelativeItem().AlignCenter().Text(d.MyWins.ToString()).FontSize(42).Bold().FontColor(Win);
                        t.AutoItem().AlignMiddle().PaddingHorizontal(4).Text("–").FontSize(30).FontColor(TextLo);
                        t.RelativeItem().AlignCenter().Text(d.OpponentWins.ToString()).FontSize(42).Bold().FontColor(Loss);
                    });

                    c.Item().PaddingTop(2).Row(l =>
                    {
                        l.RelativeItem().AlignCenter().Text("WINS").FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                        l.AutoItem().PaddingHorizontal(4).Text(" ").FontSize(8);
                        l.RelativeItem().AlignCenter().Text("WINS").FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                    });

                    if (d.Draws > 0)
                        c.Item().PaddingTop(8).AlignCenter().CornerRadius(8).Background(Bg).Border(1).BorderColor(Stroke)
                            .PaddingVertical(3).PaddingHorizontal(9)
                            .Text($"{d.Draws} draw{(d.Draws == 1 ? "" : "s")}").FontSize(8.5f).Bold().FontColor(Draw).LetterSpacing(0.1f);
                });

                Side(row, d.OpponentName, opponentAvatar);
            });
        }

        private static void Side(RowDescriptor row, string name, byte[]? avatar)
        {
            row.RelativeItem().Column(c =>
            {
                if (avatar != null)
                    c.Item().AlignCenter().Width(78).Height(78).Image(avatar).FitArea();
                else
                    c.Item().AlignCenter().Width(78).Height(78).CornerRadius(39).Background(Cell).Border(1.5f).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(name)).FontSize(28).Bold().FontColor(Accent);

                c.Item().PaddingTop(9).AlignCenter().Text(name).FontSize(12.5f).Bold().FontColor(TextHi).ClampLines(1, "…");
            });
        }

        private static void LastMeeting(ColumnDescriptor panel, VsCardData d)
        {
            string color = d.LastOutcome switch
            {
                "W" => Win,
                "D" => Draw,
                _ => Loss,
            };

            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(10).Height(10).Svg(HistoryIcon(TextLo));
                    r.AutoItem().AlignMiddle().PaddingLeft(6).Text("LAST MEETING").FontSize(8.5f).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                    r.RelativeItem().AlignMiddle().AlignRight().CornerRadius(8).Background(Bg).Border(1).BorderColor(color)
                        .PaddingVertical(3).PaddingHorizontal(9)
                        .Text(d.LastOutcome ?? "").FontSize(9).Bold().FontColor(color);
                });

                c.Item().PaddingTop(10).AlignCenter().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Text(ScoreOrDash(d.LastMyScore)).FontSize(26).Bold().FontColor(color);
                    r.AutoItem().AlignMiddle().PaddingHorizontal(10).Text("–").FontSize(22).FontColor(TextLo);
                    r.AutoItem().AlignMiddle().Text(ScoreOrDash(d.LastOpponentScore)).FontSize(26).Bold().FontColor(TextLo);
                });

                if (!string.IsNullOrWhiteSpace(d.LastTournamentName))
                    c.Item().PaddingTop(8).AlignCenter()
                        .Text($"{d.LastTournamentName} · {d.LastHubName}")
                        .FontSize(9.5f).FontColor(TextLo).ClampLines(1, "…");

                if (d.LastMatchTime.HasValue)
                    c.Item().PaddingTop(4).AlignCenter()
                        .Text(FormatUtc(d.LastMatchTime.Value, "dd MMM yyyy"))
                        .FontSize(9).Bold().FontColor(TextLo);
            });
        }

        private static void FirstMeeting(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(28).Column(c =>
                {
                    c.Item().AlignCenter().Width(30).Height(30).Svg(SwordsIcon(Accent));
                    c.Item().PaddingTop(10).AlignCenter().Text("First meeting").FontSize(14).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter().Text("You two haven't played a completed match yet.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Footer(ColumnDescriptor panel, VsCardData d)
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

        private static string SwordsIcon(string color)
            => Svg("<path d=\"M14.5 17.5 3 6V3h3l11.5 11.5\"/><path d=\"m13 19 6-6\"/><path d=\"m16 16 4 4\"/><path d=\"M19 21l2-2\"/><path d=\"M14.5 6.5 18 3h3v3l-3.5 3.5\"/><path d=\"m9 13-4 4\"/><path d=\"m5 20-2-2\"/><path d=\"M5 20l-2 2\"/>", color);

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
