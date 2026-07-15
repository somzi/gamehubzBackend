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
    /// <summary>Display data for the /nextmatch Discord card. Everything is pre-resolved
    /// so the renderer stays pure (no DB / catalog lookups inside the layout).</summary>
    public class NextMatchCardData
    {
        public string PlayerName { get; set; } = "";
        public byte[]? PlayerAvatar { get; set; }

        /// <summary>False → the "no upcoming matches" state; the match fields below are ignored.</summary>
        public bool HasMatch { get; set; }

        public string Opponent { get; set; } = "";
        public byte[]? OpponentAvatar { get; set; }
        public string Tournament { get; set; } = "";
        public string Hub { get; set; } = "";

        /// <summary>UTC. Null → "Not scheduled".</summary>
        public DateTime? ScheduledTime { get; set; }

        /// <summary>Round deadline, UTC. Null → no deadline line.</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>Render moment, UTC — shown in the footer.</summary>
        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders the /nextmatch card to a PNG via QuestPDF — same engine, palette and compact panel
    /// style as <see cref="DiscordMatchesCard"/>, but as a face-off hero: both players' circular
    /// avatars around a big VS, the tournament context and a prominent scheduled-time / deadline
    /// block. Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordNextMatchCard
    {
        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look (same as DiscordMatchesCard).
        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Loss = "#F87171";
        private const string Draw = "#FBBF24";

        public static byte[] Render(NextMatchCardData d)
        {
            byte[]? playerAvatar = TryMakeCircle(d.PlayerAvatar, 200);
            byte[]? opponentAvatar = TryMakeCircle(d.OpponentAvatar, 200);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Fixed width, dynamic height — the image hugs the panel with just a slim dark
                    // margin. Kept narrow so Discord's mobile client shows it large.
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
                                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(BoltIcon(Accent));
                                    r.AutoItem().AlignMiddle().PaddingLeft(7)
                                        .Text("NEXT MATCH").FontSize(10).Bold().FontColor(Accent).LetterSpacing(0.15f);
                                });

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                if (!d.HasMatch)
                                {
                                    EmptyState(panel);
                                }
                                else
                                {
                                    FaceOff(panel, d, playerAvatar, opponentAvatar);

                                    panel.Item().AlignCenter()
                                        .Text($"{d.Tournament} · {d.Hub}").FontSize(10).Bold().FontColor(TextLo).ClampLines(1, "…");

                                    TimeBlock(panel, d);
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

        private static void FaceOff(ColumnDescriptor panel, NextMatchCardData d, byte[]? playerAvatar, byte[]? opponentAvatar)
        {
            panel.Item().PaddingTop(4).Row(row =>
            {
                Side(row, d.PlayerName, playerAvatar);

                row.ConstantItem(70).AlignMiddle().Column(c =>
                {
                    c.Item().AlignCenter().Text("VS").FontSize(24).Bold().FontColor(Accent).LetterSpacing(0.1f);
                });

                Side(row, d.Opponent, opponentAvatar);
            });
        }

        private static void Side(RowDescriptor row, string name, byte[]? avatar)
        {
            row.RelativeItem().Column(c =>
            {
                if (avatar != null)
                    c.Item().AlignCenter().Width(84).Height(84).Image(avatar).FitArea();
                else
                    c.Item().AlignCenter().Width(84).Height(84).CornerRadius(42).Background(Cell).Border(1.5f).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(name)).FontSize(30).Bold().FontColor(Accent);

                c.Item().PaddingTop(9).AlignCenter().Text(name).FontSize(13).Bold().FontColor(TextHi).ClampLines(1, "…");
            });
        }

        private static void TimeBlock(ColumnDescriptor panel, NextMatchCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Column(c =>
            {
                if (d.ScheduledTime.HasValue)
                {
                    c.Item().AlignCenter().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().Width(14).Height(14).Svg(ClockIcon(Accent));
                        r.AutoItem().AlignMiddle().PaddingLeft(7)
                            .Text(FormatUtc(d.ScheduledTime.Value, "dd MMM yyyy · HH:mm") + " UTC")
                            .FontSize(13.5f).Bold().FontColor(Accent);
                    });
                }
                else
                {
                    c.Item().AlignCenter().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().Width(14).Height(14).Svg(HourglassIcon(Draw));
                        r.AutoItem().AlignMiddle().PaddingLeft(7)
                            .Text("Not scheduled").FontSize(13.5f).Bold().FontColor(Draw);
                    });

                    c.Item().PaddingTop(5).AlignCenter()
                        .Text("Agree on a time with your opponent in the app").FontSize(9).FontColor(TextLo);
                }

                if (d.Deadline.HasValue)
                    c.Item().PaddingTop(7).AlignCenter().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().Width(10).Height(10).Svg(FlagIcon(Loss));
                        r.AutoItem().AlignMiddle().PaddingLeft(5)
                            .Text("DEADLINE " + FormatUtc(d.Deadline.Value, "dd MMM · HH:mm"))
                            .FontSize(9).Bold().FontColor(Loss).LetterSpacing(0.08f);
                    });
            });
        }

        private static void EmptyState(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(34).Column(c =>
                {
                    c.Item().AlignCenter().Width(34).Height(34).Svg(HourglassIcon(TextLo));
                    c.Item().PaddingTop(12).AlignCenter().Text("No upcoming matches").FontSize(15).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter().Text("You're all clear — check back after the next draw.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Footer(ColumnDescriptor panel, NextMatchCardData d)
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

        // Inter has no emoji glyphs and Discord <t:...> timestamps don't work in images — plain
        // invariant-culture strings only (English month names regardless of server locale).
        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Initial(string name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

        // ── SVG line icons (Inter has no emoji glyphs, so icons are drawn as vectors) ──

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string ClockIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>", color);

        private static string HourglassIcon(string color)
            => Svg("<path d=\"M8 3h8v3c0 2.5-4 3.5-4 6s4 3.5 4 6v3H8v-3c0-2.5 4-3.5 4-6s-4-3.5-4-6V3z\"/>", color);

        private static string FlagIcon(string color)
            => Svg("<path d=\"M5 21V3\"/><path d=\"M5 4h13l-3.5 4 3.5 4H5\"/>", color);

        private static string BoltIcon(string color)
            => Svg("<path d=\"M13 2 5 13.5h6L9.5 22 18 10.5h-6L13 2z\"/>", color);

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

        // Circular-crops the avatar with pure ImageSharp (no ImageSharp.Drawing dependency): resize-
        // cover to a square, then zero the alpha outside the inscribed circle so the panel background
        // shows through the corners. Any failure (bad bytes, unreachable image) → no avatar.
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
