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
    /// <summary>Display data for the /profile Discord card. Everything is pre-resolved to strings/ints
    /// so the renderer stays pure (no DB / catalog lookups inside the layout).</summary>
    public class ProfileCardData
    {
        public string Name { get; set; } = "";
        public string Ign { get; set; } = "";
        public string Region { get; set; } = "";
        public string Country { get; set; } = "";
        public int Trophies { get; set; }
        public int Matches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int WinRate { get; set; }
        public byte[]? Avatar { get; set; }

        /// <summary>Last outcomes ("W"/"D"/"L"), oldest → latest, at most 10.</summary>
        public List<string> RecentForm { get; set; } = new();

        /// <summary>Render moment, UTC — shown in the footer.</summary>
        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders the /profile card to a PNG via QuestPDF — same engine, palette and compact panel
    /// style as <see cref="DiscordMatchesCard"/>, mirroring the mobile app's Profile screen:
    /// avatar + name header with IGN/country/region chips, a Played/Wins/Trophies/Win% strip,
    /// a "Recent form" W-D-L circle row and a win-rate donut with W/D/L bars. Pure and static —
    /// callers pass fully resolved data.
    /// </summary>
    public static class DiscordProfileCard
    {
        private const int FormSlots = 10;

        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look (same as DiscordMatchesCard).
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
        private const string Info = "#60A5FA";

        public static byte[] Render(ProfileCardData d)
        {
            byte[]? avatar = TryMakeCircle(d.Avatar, 160);

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

                                Header(panel, d, avatar);

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                StatsStrip(panel, d);
                                RecentForm(panel, d);
                                WinRateCard(panel, d);
                                Footer(panel, d);
                            });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void Header(ColumnDescriptor panel, ProfileCardData d, byte[]? avatar)
        {
            panel.Item().Row(row =>
            {
                if (avatar != null)
                    row.ConstantItem(64).Height(64).Image(avatar).FitArea();
                else
                    row.ConstantItem(64).Height(64).CornerRadius(32).Background(Cell).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(d.Name)).FontSize(24).Bold().FontColor(Accent);

                row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(c =>
                {
                    c.Item().Text(d.Name).FontSize(22).Bold().FontColor(TextHi).ClampLines(1, "…");

                    // Inlined so the chips wrap to a second line instead of overflowing.
                    c.Item().PaddingTop(7).Inlined(chips =>
                    {
                        chips.Spacing(6);
                        Chip(chips, GamepadIcon(Accent), d.Ign, Accent);
                        Chip(chips, FlagIcon(TextHi), d.Country, TextHi);
                        Chip(chips, GlobeIcon(TextLo), d.Region, TextLo);
                    });
                });
            });
        }

        private static void Chip(InlinedDescriptor chips, string icon, string text, string color)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            chips.Item().CornerRadius(9).Background(Bg).Border(1).BorderColor(Stroke)
                .PaddingVertical(4).PaddingHorizontal(8).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(9).Height(9).Svg(icon);
                    r.AutoItem().AlignMiddle().PaddingLeft(5)
                        .Text(text.ToUpperInvariant()).FontSize(8).Bold().FontColor(color).LetterSpacing(0.08f);
                });
        }

        private static void StatsStrip(ColumnDescriptor panel, ProfileCardData d)
        {
            panel.Item().Row(row =>
            {
                row.Spacing(10);
                StatCell(row, "PLAYED", d.Matches.ToString(), TextHi);
                StatCell(row, "WINS", d.Wins.ToString(), Win);
                StatCell(row, "TROPHIES", d.Trophies.ToString(), Draw);
                StatCell(row, "WIN %", d.WinRate + "%", Info);
            });
        }

        private static void StatCell(RowDescriptor row, string label, string value, string valueColor)
        {
            row.RelativeItem().CornerRadius(12).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(12).PaddingHorizontal(4).Column(c =>
                {
                    c.Item().AlignCenter().Text(value).FontSize(19).Bold().FontColor(valueColor);
                    c.Item().PaddingTop(4).AlignCenter().Text(label).FontSize(7.5f).Bold().FontColor(TextLo).LetterSpacing(0.12f);
                });
        }

        private static void RecentForm(ColumnDescriptor panel, ProfileCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Column(card =>
            {
                card.Item().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(12).Height(12).Svg(TrendIcon(Accent));
                    r.AutoItem().AlignMiddle().PaddingLeft(7)
                        .Text("RECENT FORM").FontSize(10).Bold().FontColor(TextHi).LetterSpacing(0.12f);

                    r.RelativeItem().AlignMiddle().AlignRight().Row(legend =>
                    {
                        LegendDot(legend, Win, "WIN");
                        LegendDot(legend, Draw, "DRAW");
                        LegendDot(legend, Loss, "LOSS");
                    });
                });

                card.Item().PaddingTop(13).AlignCenter().Row(r =>
                {
                    r.Spacing(14);

                    for (int i = 0; i < FormSlots; i++)
                    {
                        string? outcome = i < d.RecentForm.Count ? d.RecentForm[i] : null;
                        string color = outcome switch
                        {
                            "W" => Win,
                            "D" => Draw,
                            "L" => Loss,
                            _ => Stroke,
                        };

                        var circle = r.ConstantItem(26).Height(26).CornerRadius(13).Background(Bg)
                            .Border(1.5f).BorderColor(color);

                        if (outcome != null)
                            circle.AlignMiddle().AlignCenter().Text(outcome).FontSize(10.5f).Bold().FontColor(color);
                    }
                });

                if (d.RecentForm.Count > 0)
                    card.Item().PaddingTop(9).Row(r =>
                    {
                        r.AutoItem().Text("OLDEST").FontSize(6.5f).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                        r.RelativeItem().AlignRight().Text("LATEST").FontSize(6.5f).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                    });
                else
                    card.Item().PaddingTop(9).AlignCenter()
                        .Text("No performance data yet").FontSize(8.5f).FontColor(TextLo);
            });
        }

        private static void LegendDot(RowDescriptor legend, string color, string label)
        {
            legend.AutoItem().AlignMiddle().PaddingLeft(8).Width(6).Height(6).Svg(DotIcon(color));
            legend.AutoItem().AlignMiddle().PaddingLeft(3).Text(label).FontSize(7).Bold().FontColor(TextLo);
        }

        private static void WinRateCard(ColumnDescriptor panel, ProfileCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke).Padding(16).Column(card =>
            {
                // Donut: SVG ring underneath, centered percentage text on top.
                card.Item().AlignCenter().Width(120).Height(120).Layers(layers =>
                {
                    layers.Layer().Svg(DonutSvg(d.WinRate, Accent, Bg));
                    layers.PrimaryLayer().AlignMiddle().Column(t =>
                    {
                        t.Item().AlignCenter().Text(d.WinRate + "%").FontSize(23).Bold().FontColor(TextHi);
                        t.Item().PaddingTop(2).AlignCenter().Text("WIN RATE").FontSize(7).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                    });
                });

                card.Item().PaddingTop(14).Column(bars =>
                {
                    bars.Spacing(10);
                    Bar(bars, "WINS", d.Wins, d.Matches, Win);
                    Bar(bars, "DRAWS", d.Draws, d.Matches, Draw);
                    Bar(bars, "LOSSES", d.Losses, d.Matches, Loss);
                });
            });
        }

        private static void Bar(ColumnDescriptor bars, string label, int value, int total, string color)
        {
            float ratio = total <= 0 ? 0 : Math.Clamp(value / (float)total, 0f, 1f);

            bars.Item().Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(6).Height(6).Svg(DotIcon(color));
                    r.AutoItem().AlignMiddle().PaddingLeft(6).Text(label).FontSize(8.5f).Bold().FontColor(TextLo).LetterSpacing(0.1f);
                    r.RelativeItem().AlignMiddle().AlignRight().Text(value.ToString()).FontSize(9.5f).Bold().FontColor(color);
                });

                c.Item().PaddingTop(5).Height(6).CornerRadius(3).Background(Bg).Row(track =>
                {
                    if (ratio >= 1f)
                        track.RelativeItem().CornerRadius(3).Background(color);
                    else if (ratio > 0f)
                    {
                        track.RelativeItem(ratio).CornerRadius(3).Background(color);
                        track.RelativeItem(1f - ratio);
                    }
                    else
                        track.RelativeItem();
                });
            });
        }

        private static void Footer(ColumnDescriptor panel, ProfileCardData d)
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

        // Inter has no emoji glyphs — plain invariant-culture strings only (English month names
        // regardless of server locale).
        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Initial(string name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

        // ── SVG line icons (Inter has no emoji glyphs, so icons are drawn as vectors) ──

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string GamepadIcon(string color)
            => Svg("<rect x=\"2\" y=\"7\" width=\"20\" height=\"10\" rx=\"5\"/><path d=\"M7 10v4M5 12h4\"/><circle cx=\"15.5\" cy=\"13.5\" r=\"0.5\"/><circle cx=\"18.5\" cy=\"10.5\" r=\"0.5\"/>", color);

        private static string FlagIcon(string color)
            => Svg("<path d=\"M5 21V3\"/><path d=\"M5 4h13l-3.5 4 3.5 4H5\"/>", color);

        private static string GlobeIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M3 12h18M12 3c2.5 2.5 2.5 15.5 0 18M12 3c-2.5 2.5-2.5 15.5 0 18\"/>", color);

        private static string TrendIcon(string color)
            => Svg("<path d=\"M3 17l6-6 4 4 8-8\"/><path d=\"M14 7h7v7\"/>", color);

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

        private static string DotIcon(string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\"><circle cx=\"4\" cy=\"4\" r=\"3\" fill=\"{color}\"/></svg>";

        // Win-rate ring: a track circle plus a percentage arc, rotated so it starts at 12 o'clock.
        // At 0% the arc is omitted entirely — a zero-length dash with round caps still paints a dot.
        private static string DonutSvg(int percent, string color, string track)
        {
            double radius = 52;
            double circumference = 2 * Math.PI * radius;
            double filled = Math.Clamp(percent, 0, 100) / 100.0 * circumference;

            string dash = filled.ToString("0.##", CultureInfo.InvariantCulture);
            string gap = circumference.ToString("0.##", CultureInfo.InvariantCulture);

            string arc = percent <= 0
                ? ""
                : $"<circle cx=\"60\" cy=\"60\" r=\"52\" fill=\"none\" stroke=\"{color}\" stroke-width=\"10\" stroke-linecap=\"round\" stroke-dasharray=\"{dash} {gap}\" transform=\"rotate(-90 60 60)\"/>";

            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 120\">"
                + $"<circle cx=\"60\" cy=\"60\" r=\"52\" fill=\"none\" stroke=\"{track}\" stroke-width=\"10\"/>"
                + arc
                + "</svg>";
        }

        // Circular-crops the avatar with pure ImageSharp (no ImageSharp.Drawing dependency): resize-
        // cover to a square, then zero the alpha outside the inscribed circle so the card background
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
