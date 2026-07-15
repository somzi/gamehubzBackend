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

        /// <summary>Hub creation date — the "Established" row.</summary>
        public DateTime? EstablishedOn { get; set; }

        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders the /hubinfo card to a PNG — mirrors the mobile app's Hub → Overview screen:
    /// avatar + name header with a verified badge, then a "General info" list (Members,
    /// Tournaments, Access, Established, Owner), each row with a colored icon chip on the left
    /// and the value on the right. Pure and static — callers pass fully resolved data.
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
                                    panel.Item().Text(d.Description).FontSize(10.5f).FontColor(TextLo).ClampLines(2, "…");

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                // ── General info — same list the mobile Hub → Overview shows ──
                                panel.Item().Row(r =>
                                {
                                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(InfoIcon(TrophyGold));
                                    r.AutoItem().AlignMiddle().PaddingLeft(7)
                                        .Text("GENERAL INFO").FontSize(10).Bold().FontColor(TextLo).LetterSpacing(0.15f);
                                });

                                panel.Item().Column(list =>
                                {
                                    list.Spacing(8);
                                    InfoRow(list, PeopleIcon(Info), "Members", d.MembersCount.ToString(), TextHi);
                                    InfoRow(list, TrophyIcon(TrophyGold), "Tournaments", d.TournamentsCount.ToString(), TextHi);
                                    InfoRow(list, GlobeIcon(Accent), "Access", d.IsPublic ? "Public" : "Private", TextHi);
                                    if (d.EstablishedOn.HasValue)
                                        InfoRow(list, CalendarIcon(Info), "Established",
                                            FormatUtc(d.EstablishedOn.Value, "dd MMM yyyy"), TextHi);
                                    InfoRow(list, PersonIcon(Accent), "Owner", d.OwnerName, Accent);
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

        private static void Header(ColumnDescriptor panel, HubInfoCardData d, byte[]? avatar)
        {
            panel.Item().Row(row =>
            {
                if (avatar != null)
                    row.ConstantItem(64).Height(64).Image(avatar).FitArea();
                else
                    row.ConstantItem(64).Height(64).CornerRadius(32).Background(Cell).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(d.Name)).FontSize(24).Bold().FontColor(Accent);

                row.RelativeItem().PaddingLeft(15).AlignMiddle().Column(c =>
                {
                    c.Item().Text("HUB PROFILE").FontSize(9.5f).Bold().FontColor(Accent).LetterSpacing(0.18f);

                    c.Item().PaddingTop(3).Row(r =>
                    {
                        r.AutoItem().Text(d.Name).FontSize(22).Bold().FontColor(TextHi).ClampLines(1, "…");
                        if (d.IsVerified)
                            r.AutoItem().PaddingLeft(6).AlignMiddle().Width(15).Height(15).Svg(VerifiedIcon(Info));
                    });
                });
            });
        }

        // One "General info" row: colored icon chip, label, value pushed to the right —
        // the same layout the mobile Hub → Overview list uses.
        private static void InfoRow(ColumnDescriptor list, string icon, string label, string value, string valueColor)
        {
            list.Item().CornerRadius(12).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(9).PaddingHorizontal(12).Row(row =>
                {
                    row.ConstantItem(26).Height(26).CornerRadius(8).Background(Bg).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Width(13).Height(13).Svg(icon);

                    row.RelativeItem().PaddingLeft(11).AlignMiddle()
                        .Text(label).FontSize(11.5f).Bold().FontColor(TextHi);

                    row.AutoItem().AlignMiddle()
                        .Text(value).FontSize(12).Bold().FontColor(valueColor);
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

        private static string PeopleIcon(string color)
            => Svg("<circle cx=\"9\" cy=\"8\" r=\"3.5\"/><path d=\"M2.5 20c0-3.5 3-5.5 6.5-5.5s6.5 2 6.5 5.5\"/><path d=\"M16 5a3.5 3.5 0 0 1 0 6.3M18 14.8c2.1.8 3.5 2.4 3.5 5.2\"/>", color);

        private static string GlobeIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M3 12h18M12 3c2.5 2.5 2.5 15.5 0 18M12 3c-2.5 2.5-2.5 15.5 0 18\"/>", color);

        private static string CalendarIcon(string color)
            => Svg("<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M3 10h18M8 3v4M16 3v4\"/>", color);

        private static string PersonIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21c0-4 4-7 8-7s8 3 8 7\"/>", color);

        private static string InfoIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v5\"/><circle cx=\"12\" cy=\"8\" r=\"0.5\"/>", color);

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
