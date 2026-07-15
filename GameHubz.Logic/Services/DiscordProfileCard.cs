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
    }

    /// <summary>
    /// Renders the /profile card to a PNG via QuestPDF — the same engine the tournament PDF export
    /// uses, so the Inter font + Community license set at startup apply here too. Dark, premium
    /// layout: circular avatar + name header, an IGN / Region / Country / Trophies strip, and a
    /// five-cell tournament-stats row. Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordProfileCard
    {
        private const string Font = "Inter";

        // Dark palette mirroring the app's navy/emerald look.
        private const string Bg = "#0A0F1E";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Win = "#34D399";
        private const string Loss = "#F87171";
        private const string Draw = "#FBBF24";

        public static byte[] Render(ProfileCardData d)
        {
            byte[]? avatar = TryMakeCircle(d.Avatar, 220);

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
                        col.Spacing(20);

                        // ── Header: avatar + name ──
                        col.Item().Row(row =>
                        {
                            if (avatar != null)
                                row.ConstantItem(88).Height(88).Image(avatar).FitArea();
                            else
                                row.ConstantItem(88).Height(88).Background(Cell).Border(1).BorderColor(Stroke);

                            row.RelativeItem().PaddingLeft(20).AlignMiddle().Column(c =>
                            {
                                c.Item().Text("PLAYER PROFILE").FontSize(10).Bold().FontColor(Accent).LetterSpacing(0.15f);
                                c.Item().PaddingTop(4).Text(d.Name).FontSize(28).Bold().FontColor(TextHi).ClampLines(1, "…");
                            });
                        });

                        // ── Info strip: IGN | REGION | COUNTRY | TROPHIES ──
                        col.Item().Row(row =>
                        {
                            row.Spacing(12);
                            InfoCell(row, "IGN", d.Ign);
                            InfoCell(row, "REGION", d.Region);
                            InfoCell(row, "COUNTRY", d.Country);
                            InfoCell(row, "TROPHIES", d.Trophies.ToString());
                        });

                        // ── Tournament stats ──
                        col.Item().Column(c =>
                        {
                            c.Item().Text("TOURNAMENT STATS").FontSize(11).Bold().FontColor(TextLo).LetterSpacing(0.12f);
                            c.Item().PaddingTop(10).Row(row =>
                            {
                                row.Spacing(12);
                                StatCell(row, "MATCHES", d.Matches.ToString(), TextHi);
                                StatCell(row, "WINS", d.Wins.ToString(), Win);
                                StatCell(row, "LOSSES", d.Losses.ToString(), Loss);
                                StatCell(row, "DRAWS", d.Draws.ToString(), Draw);
                                StatCell(row, "W/L %", d.WinRate + "%", Accent);
                            });
                        });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void InfoCell(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Background(Cell).Border(1).BorderColor(Stroke).Padding(13).Column(c =>
            {
                c.Item().Text(label).FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.12f);
                c.Item().PaddingTop(5).Text(value).FontSize(15).Bold().FontColor(TextHi).ClampLines(1, "…");
            });
        }

        private static void StatCell(RowDescriptor row, string label, string value, string valueColor)
        {
            row.RelativeItem().Background(Cell).Border(1).BorderColor(Stroke).PaddingVertical(14).PaddingHorizontal(6).Column(c =>
            {
                c.Item().AlignCenter().Text(label).FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.1f);
                c.Item().PaddingTop(7).AlignCenter().Text(value).FontSize(22).Bold().FontColor(valueColor);
            });
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
