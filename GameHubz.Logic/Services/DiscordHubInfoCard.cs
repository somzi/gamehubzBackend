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
    public class HubInfoCardData
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public byte[]? Avatar { get; set; }
        public string OwnerName { get; set; } = "";
        public bool IsVerified { get; set; }
        public bool IsPublic { get; set; }

        public int MembersCount { get; set; }
        public int TournamentsCount { get; set; }
        public int ChampionsCount { get; set; }

        public string? NextTournamentName { get; set; }
        public DateTime? NextTournamentDate { get; set; }

        public string? LatestChampionName { get; set; }
        public string? LatestChampionTournament { get; set; }

        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders the /hubinfo poster card to a PNG — same compact panel style as the other Discord
    /// cards. Big hub avatar + verification badge, description snippet, a Members/Tournaments/
    /// Champions stats strip, and two feature cards for the next scheduled tournament and the
    /// latest champion. Pure and static — callers pass fully resolved data.
    /// </summary>
    public static class DiscordHubInfoCard
    {
        private const string Font = "Inter";

        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Info = "#60A5FA";
        private const string TrophyGold = "#FBBF24";

        public static byte[] Render(HubInfoCardData d)
        {
            byte[]? avatar = TryMakeCircle(d.Avatar, 200);

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

                                Header(panel, d, avatar);

                                if (!string.IsNullOrWhiteSpace(d.Description))
                                    panel.Item().Text(d.Description).FontSize(10.5f).FontColor(TextLo).ClampLines(3, "…");

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                StatsStrip(panel, d);

                                NextTournament(panel, d);
                                LatestChampion(panel, d);

                                Footer(panel, d);
                            });
                    });
                });
            });

            return document
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .First();
        }

        private static void Header(ColumnDescriptor panel, HubInfoCardData d, byte[]? avatar)
        {
            panel.Item().Row(row =>
            {
                if (avatar != null)
                    row.ConstantItem(72).Height(72).Image(avatar).FitArea();
                else
                    row.ConstantItem(72).Height(72).CornerRadius(36).Background(Cell).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(d.Name)).FontSize(26).Bold().FontColor(Accent);

                row.RelativeItem().PaddingLeft(15).AlignMiddle().Column(c =>
                {
                    c.Item().Text("HUB PROFILE").FontSize(9.5f).Bold().FontColor(Accent).LetterSpacing(0.18f);

                    c.Item().PaddingTop(3).Row(r =>
                    {
                        r.AutoItem().Text(d.Name).FontSize(22).Bold().FontColor(TextHi).ClampLines(1, "…");
                        if (d.IsVerified)
                            r.AutoItem().PaddingLeft(6).AlignMiddle().Width(15).Height(15).Svg(VerifiedIcon(Info));
                    });

                    c.Item().PaddingTop(5).Inlined(chips =>
                    {
                        chips.Spacing(6);
                        MetaChip(chips, PersonIcon(TextLo), d.OwnerName);
                        MetaChip(chips, LockIcon(TextLo), d.IsPublic ? "PUBLIC" : "PRIVATE");
                    });
                });
            });
        }

        private static void MetaChip(InlinedDescriptor chips, string icon, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            chips.Item().CornerRadius(8).Background(Bg).Border(1).BorderColor(Stroke)
                .PaddingVertical(3).PaddingHorizontal(7).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(9).Height(9).Svg(icon);
                    r.AutoItem().AlignMiddle().PaddingLeft(5)
                        .Text(text.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(TextLo).LetterSpacing(0.08f);
                });
        }

        private static void StatsStrip(ColumnDescriptor panel, HubInfoCardData d)
        {
            panel.Item().Row(row =>
            {
                row.Spacing(10);
                StatCell(row, "MEMBERS", d.MembersCount.ToString(), TextHi);
                StatCell(row, "TOURNAMENTS", d.TournamentsCount.ToString(), Accent);
                StatCell(row, "CHAMPIONS", d.ChampionsCount.ToString(), TrophyGold);
            });
        }

        private static void StatCell(RowDescriptor row, string label, string value, string valueColor)
        {
            row.RelativeItem().CornerRadius(12).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(12).PaddingHorizontal(4).Column(c =>
                {
                    c.Item().AlignCenter().Text(value).FontSize(21).Bold().FontColor(valueColor);
                    c.Item().PaddingTop(4).AlignCenter().Text(label).FontSize(7.5f).Bold().FontColor(TextLo).LetterSpacing(0.12f);
                });
        }

        private static void NextTournament(ColumnDescriptor panel, HubInfoCardData d)
        {
            panel.Item().CornerRadius(14).Background(Cell).Border(1).BorderColor(Stroke).Padding(12).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(11).Height(11).Svg(CalendarIcon(Accent));
                    r.AutoItem().AlignMiddle().PaddingLeft(6)
                        .Text("NEXT TOURNAMENT").FontSize(9).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                });

                if (!string.IsNullOrWhiteSpace(d.NextTournamentName))
                {
                    c.Item().PaddingTop(6).Text(d.NextTournamentName).FontSize(14).Bold().FontColor(TextHi).ClampLines(1, "…");
                    if (d.NextTournamentDate.HasValue)
                        c.Item().PaddingTop(2)
                            .Text(FormatUtc(d.NextTournamentDate.Value, "dd MMM yyyy · HH:mm") + " UTC")
                            .FontSize(10).Bold().FontColor(Accent);
                }
                else
                    c.Item().PaddingTop(6).Text("No scheduled tournaments").FontSize(11).FontColor(TextLo);
            });
        }

        private static void LatestChampion(ColumnDescriptor panel, HubInfoCardData d)
        {
            panel.Item().CornerRadius(14).Background(Cell).Border(1).BorderColor(Stroke).Padding(12).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(11).Height(11).Svg(TrophyIcon(TrophyGold));
                    r.AutoItem().AlignMiddle().PaddingLeft(6)
                        .Text("LATEST CHAMPION").FontSize(9).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                });

                if (!string.IsNullOrWhiteSpace(d.LatestChampionName))
                {
                    c.Item().PaddingTop(6).Text(d.LatestChampionName).FontSize(14).Bold().FontColor(TrophyGold).ClampLines(1, "…");
                    if (!string.IsNullOrWhiteSpace(d.LatestChampionTournament))
                        c.Item().PaddingTop(2).Text(d.LatestChampionTournament).FontSize(10).FontColor(TextLo).ClampLines(1, "…");
                }
                else
                    c.Item().PaddingTop(6).Text("No champions crowned yet").FontSize(11).FontColor(TextLo);
            });
        }

        private static void Footer(ColumnDescriptor panel, HubInfoCardData d)
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

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

        private static string CalendarIcon(string color)
            => Svg("<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M3 10h18M8 3v4M16 3v4\"/>", color);

        private static string PersonIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21c0-4 4-7 8-7s8 3 8 7\"/>", color);

        private static string LockIcon(string color)
            => Svg("<rect x=\"5\" y=\"11\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>", color);

        private static string VerifiedIcon(string color)
            => Svg("<path d=\"M12 2l2.5 2.5L18 4l1 3.5L21.5 10 20 12l1.5 2-2.5 2.5L18 20l-3.5-1L12 22l-2.5-3-3.5 1-1-3.5L2.5 14 4 12 2.5 10 5 7.5 6 4l3.5 1L12 2z\"/><path d=\"m9 12 2 2 4-4\"/>", color);

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
