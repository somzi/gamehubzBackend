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
    /// <summary>Display data for the /matches Discord card. Everything is pre-resolved
    /// so the renderer stays pure (no DB / catalog lookups inside the layout).</summary>
    public class MatchesCardData
    {
        public string Name { get; set; } = "";

        /// <summary>The invoking player's avatar (shown in the header).</summary>
        public byte[]? Avatar { get; set; }

        /// <summary>Total active matches — can exceed Rows.Count, in which case a "+N more" row is added.</summary>
        public int TotalActive { get; set; }

        public List<MatchesCardRow> Rows { get; set; } = new();

        /// <summary>Render moment, UTC — shown in the footer.</summary>
        public DateTime GeneratedAtUtc { get; set; }
    }

    public class MatchesCardRow
    {
        public string Opponent { get; set; } = "";
        public string Tournament { get; set; } = "";
        public string Hub { get; set; } = "";

        /// <summary>UTC. Null → "Not scheduled".</summary>
        public DateTime? ScheduledTime { get; set; }

        /// <summary>Round deadline, UTC. Null → no deadline line.</summary>
        public DateTime? Deadline { get; set; }

        public byte[]? Avatar { get; set; }
    }

    /// <summary>
    /// Renders the /matches card to a PNG via QuestPDF — same engine, palette and Inter font as
    /// <see cref="DiscordProfileCard"/>, but in a portrait "poster" layout so Discord's mobile
    /// client shows it large: one rounded panel with a player header, a match-schedule list
    /// (circular opponent avatars, scheduled time + round deadline with SVG line icons) and a
    /// branded footer. Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordMatchesCard
    {
        /// <summary>Rows rendered on the card; anything beyond collapses into a "+N more" line.</summary>
        public const int MaxRows = 12;

        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look (same as DiscordProfileCard),
        // plus a slightly lighter panel tone so the card reads as a framed poster.
        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Loss = "#F87171";
        private const string Draw = "#FBBF24";

        public static byte[] Render(MatchesCardData d)
        {
            byte[]? avatar = TryMakeCircle(d.Avatar, 160);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Fixed width, dynamic height — the image hugs the panel with just a slim dark
                    // margin (no poster whitespace). Kept narrow so Discord's mobile client, which
                    // scales images to chat width, still shows it large.
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

                                Header(panel, d, avatar);

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                if (d.Rows.Count == 0)
                                {
                                    EmptyState(panel);
                                    return;
                                }

                                // ── Section label + match rows ──
                                panel.Item().Row(r =>
                                {
                                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(BoltIcon(Accent));
                                    r.AutoItem().AlignMiddle().PaddingLeft(7)
                                        .Text("MATCH SCHEDULE").FontSize(10).Bold().FontColor(TextLo).LetterSpacing(0.15f);
                                });

                                panel.Item().Column(list =>
                                {
                                    list.Spacing(10);

                                    foreach (var match in d.Rows.Take(MaxRows))
                                        MatchRow(list, match);

                                    int more = d.TotalActive - Math.Min(d.Rows.Count, MaxRows);
                                    if (more > 0)
                                        list.Item().PaddingTop(4).AlignCenter()
                                            .Text($"+{more} more — open the app").FontSize(10).FontColor(TextLo);
                                });

                                Footer(panel, d);
                            });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void Header(ColumnDescriptor panel, MatchesCardData d, byte[]? avatar)
        {
            panel.Item().Row(row =>
            {
                if (avatar != null)
                    row.ConstantItem(64).Height(64).Image(avatar).FitArea();
                else
                    row.ConstantItem(64).Height(64).CornerRadius(32).Background(Cell).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(d.Name)).FontSize(24).Bold().FontColor(Accent);

                row.RelativeItem().PaddingLeft(16).AlignMiddle().Column(c =>
                {
                    c.Item().Text(d.Name).FontSize(23).Bold().FontColor(TextHi).ClampLines(1, "…");
                    c.Item().PaddingTop(4).Text("ACTIVE MATCHES").FontSize(9.5f).Bold().FontColor(Accent).LetterSpacing(0.18f);
                });

                if (d.TotalActive > 0)
                    row.ConstantItem(46).Height(46).CornerRadius(23).Border(1.5f).BorderColor(Accent)
                        .AlignMiddle().AlignCenter().Text(d.TotalActive.ToString()).FontSize(17).Bold().FontColor(Accent);
            });
        }

        private static void MatchRow(ColumnDescriptor list, MatchesCardRow m)
        {
            byte[]? avatar = TryMakeCircle(m.Avatar, 120);

            list.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Row(row =>
            {
                // Circular opponent avatar; fallback: initial letter in a neon-outlined ring.
                if (avatar != null)
                    row.ConstantItem(46).Height(46).Image(avatar).FitArea();
                else
                    row.ConstantItem(46).Height(46).CornerRadius(23).Background(Bg).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(m.Opponent)).FontSize(18).Bold().FontColor(Accent);

                // Opponent + tournament context. The name is its own clamped block so a long
                // nickname ellipsizes instead of pushing "vs" onto a line of its own.
                row.RelativeItem().PaddingLeft(13).AlignMiddle().Column(c =>
                {
                    c.Item().Row(nr =>
                    {
                        nr.AutoItem().AlignBottom().PaddingBottom(2).PaddingRight(6)
                            .Text("vs").FontSize(11).Bold().FontColor(Accent);
                        nr.RelativeItem().Text(m.Opponent).FontSize(16).Bold().FontColor(TextHi).ClampLines(1, "…");
                    });
                    c.Item().PaddingTop(3).Text($"{m.Tournament} · {m.Hub}").FontSize(10).FontColor(TextLo).ClampLines(1, "…");
                });

                // Time info: scheduled (or "Not scheduled") + optional round deadline.
                row.ConstantItem(155).AlignMiddle().Column(c =>
                {
                    c.Spacing(5);

                    if (m.ScheduledTime.HasValue)
                        TimeChip(c, ClockIcon(Accent), FormatUtc(m.ScheduledTime.Value, "dd MMM · HH:mm") + " UTC", Accent, 9.5f);
                    else
                        TimeChip(c, HourglassIcon(Draw), "Not scheduled", Draw, 9.5f);

                    if (m.Deadline.HasValue)
                        TimeChip(c, FlagIcon(Loss), "DEADLINE " + FormatUtc(m.Deadline.Value, "dd MMM · HH:mm"), Loss, 8.5f);
                });
            });
        }

        private static void TimeChip(ColumnDescriptor c, string icon, string text, string color, float fontSize)
        {
            c.Item().AlignRight().CornerRadius(9).Background(Bg).Border(1).BorderColor(Stroke)
                .PaddingVertical(4).PaddingHorizontal(8).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(10).Height(10).Svg(icon);
                    r.AutoItem().AlignMiddle().PaddingLeft(5).Text(text).FontSize(fontSize).Bold().FontColor(color);
                });
        }

        private static void EmptyState(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(34).Column(c =>
                {
                    c.Item().AlignCenter().Width(34).Height(34).Svg(HourglassIcon(TextLo));
                    c.Item().PaddingTop(12).AlignCenter().Text("No active matches").FontSize(15).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter().Text("You're all clear — check back after the next draw.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Footer(ColumnDescriptor panel, MatchesCardData d)
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
        // cover to a square, then zero the alpha outside the inscribed circle so the row background
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
