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

        /// <summary>Total active matches — can exceed Rows.Count, in which case a "+N more" row is added.</summary>
        public int TotalActive { get; set; }

        public List<MatchesCardRow> Rows { get; set; } = new();
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
    /// <see cref="DiscordProfileCard"/>. Dark, premium layout: name header with an active-match
    /// count, then one full-width row per match (circular opponent avatar, opponent + tournament
    /// context, scheduled time and round deadline on the right). Pure and static — callers pass
    /// fully resolved data.
    /// </summary>
    public static class DiscordMatchesCard
    {
        /// <summary>Rows rendered on the card; anything beyond collapses into a "+N more" line.</summary>
        public const int MaxRows = 12;

        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look (same as DiscordProfileCard).
        private const string Bg = "#0A0F1E";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Loss = "#F87171";
        private const string Draw = "#FBBF24";

        public static byte[] Render(MatchesCardData d)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Fixed width, dynamic height — the card grows to fit its content.
                    page.ContinuousSize(760);
                    page.PageColor(Bg);
                    page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(12).FontColor(TextHi));

                    page.Content().Padding(30).Column(col =>
                    {
                        col.Spacing(18);

                        // ── Header: caption + name ──
                        col.Item().Column(c =>
                        {
                            c.Item().Text($"ACTIVE MATCHES · {d.TotalActive}").FontSize(10).Bold().FontColor(Accent).LetterSpacing(0.15f);
                            c.Item().PaddingTop(4).Text(d.Name).FontSize(28).Bold().FontColor(TextHi).ClampLines(1, "…");
                        });

                        if (d.Rows.Count == 0)
                        {
                            col.Item().Background(Cell).Border(1).BorderColor(Stroke).Padding(26)
                                .AlignCenter().Text("No active matches").FontSize(14).FontColor(TextLo);
                            return;
                        }

                        // ── Match rows ──
                        col.Item().Column(list =>
                        {
                            list.Spacing(10);

                            foreach (var match in d.Rows.Take(MaxRows))
                                MatchRow(list, match);

                            int more = d.TotalActive - Math.Min(d.Rows.Count, MaxRows);
                            if (more > 0)
                                list.Item().PaddingTop(4).AlignCenter()
                                    .Text($"+{more} more — open the app").FontSize(10).FontColor(TextLo);
                        });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void MatchRow(ColumnDescriptor list, MatchesCardRow m)
        {
            byte[]? avatar = TryMakeCircle(m.Avatar, 120);

            list.Item().Background(Cell).Border(1).BorderColor(Stroke).Padding(14).Row(row =>
            {
                // Circular opponent avatar; fallback: initial letter on a darker square.
                if (avatar != null)
                    row.ConstantItem(42).Height(42).Image(avatar).FitArea();
                else
                    row.ConstantItem(42).Height(42).Background(Bg).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter()
                        .Text(Initial(m.Opponent)).FontSize(17).Bold().FontColor(Accent);

                // Opponent + tournament context.
                row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(c =>
                {
                    c.Item().Text($"vs {m.Opponent}").FontSize(15).Bold().FontColor(TextHi).ClampLines(1, "…");
                    c.Item().PaddingTop(3).Text($"{m.Tournament} · {m.Hub}").FontSize(10).FontColor(TextLo).ClampLines(1, "…");
                });

                // Time badge: scheduled time (or "Not scheduled") + optional round deadline.
                row.ConstantItem(170).AlignMiddle().Column(c =>
                {
                    if (m.ScheduledTime.HasValue)
                        c.Item().AlignRight().Text(FormatUtc(m.ScheduledTime.Value, "dd MMM yyyy · HH:mm") + " UTC")
                            .FontSize(11).Bold().FontColor(Accent);
                    else
                        c.Item().AlignRight().Text("Not scheduled").FontSize(11).Bold().FontColor(Draw);

                    if (m.Deadline.HasValue)
                        c.Item().PaddingTop(4).AlignRight()
                            .Text("DEADLINE " + FormatUtc(m.Deadline.Value, "dd MMM · HH:mm"))
                            .FontSize(8.5f).Bold().FontColor(Loss).LetterSpacing(0.08f);
                });
            });
        }

        // Inter has no emoji glyphs and Discord <t:...> timestamps don't work in images — plain
        // invariant-culture strings only (English month names regardless of server locale).
        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Initial(string name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

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
