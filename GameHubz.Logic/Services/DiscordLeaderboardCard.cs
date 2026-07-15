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
    /// <summary>Sort mode for the /leaderboard card — dictates which metric is bold, how rows are
    /// ordered, and which secondary chips appear.</summary>
    public enum LeaderboardSort
    {
        Trophies = 0,
        Wins = 1,
        WinRate = 2,
    }

    public class LeaderboardCardData
    {
        public string HubName { get; set; } = "";
        public byte[]? HubAvatar { get; set; }

        public LeaderboardSort Sort { get; set; }
        public List<LeaderboardCardRow> Rows { get; set; } = new();

        public DateTime GeneratedAtUtc { get; set; }
    }

    public class LeaderboardCardRow
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public byte[]? Avatar { get; set; }

        public int Trophies { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int TotalMatches { get; set; }
        public int WinRate { get; set; }
    }

    /// <summary>
    /// Renders the /leaderboard card to a PNG — same compact panel style as the other Discord
    /// cards. Header shows the hub name and the active sort mode; each row has a rank badge, a
    /// circular player avatar, the player name, the primary metric (bold, in the metric's color)
    /// and secondary metric chips. Empty state when a hub has no completed matches yet.
    /// </summary>
    public static class DiscordLeaderboardCard
    {
        public const int MaxRows = 10;

        private const string Font = "Inter";

        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Win = "#34D399";
        private const string TrophyGold = "#FBBF24";
        private const string RankSilver = "#CBD5E1";
        private const string RankBronze = "#F59E0B";
        private const string Info = "#60A5FA";

        public static byte[] Render(LeaderboardCardData d)
        {
            byte[]? hubAvatar = TryMakeCircle(d.HubAvatar, 160);

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
                                panel.Spacing(14);

                                Header(panel, d, hubAvatar);

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                if (d.Rows.Count == 0)
                                    EmptyState(panel);
                                else
                                    panel.Item().Column(list =>
                                    {
                                        list.Spacing(8);
                                        foreach (var row in d.Rows.Take(MaxRows))
                                            LeaderboardRow(list, row, d.Sort);
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

        private static void Header(ColumnDescriptor panel, LeaderboardCardData d, byte[]? hubAvatar)
        {
            panel.Item().Row(row =>
            {
                if (hubAvatar != null)
                    row.ConstantItem(52).Height(52).Image(hubAvatar).FitArea();
                else
                    row.ConstantItem(52).Height(52).CornerRadius(26).Background(Cell).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(d.HubName)).FontSize(20).Bold().FontColor(Accent);

                row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(c =>
                {
                    c.Item().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().Width(12).Height(12).Svg(TrophyIcon(Accent));
                        r.AutoItem().AlignMiddle().PaddingLeft(6)
                            .Text("LEADERBOARD").FontSize(9.5f).Bold().FontColor(Accent).LetterSpacing(0.18f);
                    });
                    c.Item().PaddingTop(3).Text(d.HubName).FontSize(20).Bold().FontColor(TextHi).ClampLines(1, "…");
                });

                row.ConstantItem(115).AlignMiddle().AlignRight().CornerRadius(10).Background(Cell).Border(1).BorderColor(Stroke)
                    .PaddingVertical(6).PaddingHorizontal(9).Column(c =>
                {
                    c.Item().AlignCenter().Text(SortLabel(d.Sort)).FontSize(10).Bold().FontColor(SortColor(d.Sort)).LetterSpacing(0.12f);
                    c.Item().PaddingTop(2).AlignCenter().Text("SORT").FontSize(6.5f).Bold().FontColor(TextLo).LetterSpacing(0.14f);
                });
            });
        }

        private static void LeaderboardRow(ColumnDescriptor list, LeaderboardCardRow r, LeaderboardSort sort)
        {
            byte[]? avatar = TryMakeCircle(r.Avatar, 100);
            string rankColor = r.Rank switch { 1 => TrophyGold, 2 => RankSilver, 3 => RankBronze, _ => TextLo };
            string primaryValue = sort switch
            {
                LeaderboardSort.Wins => r.Wins.ToString(),
                LeaderboardSort.WinRate => r.WinRate + "%",
                _ => r.Trophies.ToString(),
            };
            string primaryColor = SortColor(sort);

            list.Item().CornerRadius(14).Background(Cell).Border(1).BorderColor(Stroke).Padding(10).Row(row =>
            {
                // Rank badge — gold/silver/bronze for the podium, muted for the rest.
                row.ConstantItem(34).Height(34).CornerRadius(17).Background(Bg).Border(1.5f).BorderColor(rankColor)
                    .AlignMiddle().AlignCenter().Text(r.Rank.ToString()).FontSize(13).Bold().FontColor(rankColor);

                // Player avatar (fallback = initial).
                if (avatar != null)
                    row.ConstantItem(34).PaddingLeft(8).Height(34).Image(avatar).FitArea();
                else
                    row.ConstantItem(34).PaddingLeft(8).Column(c => c.Item().Height(34).CornerRadius(17).Background(Bg).Border(1).BorderColor(Stroke)
                        .AlignMiddle().AlignCenter().Text(Initial(r.Name)).FontSize(13).Bold().FontColor(Accent));

                // Name + secondary metrics.
                row.RelativeItem().PaddingLeft(10).AlignMiddle().Column(c =>
                {
                    c.Item().Text(r.Name).FontSize(13).Bold().FontColor(TextHi).ClampLines(1, "…");
                    c.Item().PaddingTop(2).Inlined(chips =>
                    {
                        chips.Spacing(6);
                        foreach (var (label, value, color) in SecondaryMetrics(r, sort))
                            SecondaryChip(chips, label, value, color);
                    });
                });

                // Primary metric — big, in the metric's colour.
                row.ConstantItem(64).AlignMiddle().AlignRight()
                    .Text(primaryValue).FontSize(20).Bold().FontColor(primaryColor);
            });
        }

        private static IEnumerable<(string label, string value, string color)> SecondaryMetrics(LeaderboardCardRow r, LeaderboardSort sort)
        {
            // Show the two metrics NOT selected as sort, so every card carries the full picture.
            // Inter has no emoji glyphs — every label is plain ASCII so Windows/Linux servers
            // render identically without a system-emoji fallback.
            if (sort != LeaderboardSort.Trophies)
                yield return ("T", r.Trophies.ToString(), TrophyGold);
            if (sort != LeaderboardSort.Wins)
                yield return ("W", r.Wins.ToString(), Win);
            if (sort != LeaderboardSort.WinRate)
                yield return ("W%", r.WinRate + "%", Info);

            // Always end with total matches — context for winrate is essential.
            yield return ("MP", r.TotalMatches.ToString(), TextLo);
        }

        private static void SecondaryChip(InlinedDescriptor chips, string label, string value, string color)
        {
            chips.Item().Row(r =>
            {
                r.AutoItem().Text(label).FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.08f);
                r.AutoItem().PaddingLeft(3).Text(value).FontSize(8.5f).Bold().FontColor(color);
            });
        }

        private static void EmptyState(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(34).Column(c =>
                {
                    c.Item().AlignCenter().Width(34).Height(34).Svg(TrophyIcon(TextLo));
                    c.Item().PaddingTop(12).AlignCenter().Text("No completed matches yet").FontSize(15).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter().Text("Play matches in this hub and the ranking will show here.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Footer(ColumnDescriptor panel, LeaderboardCardData d)
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

        private static string SortLabel(LeaderboardSort sort) => sort switch
        {
            LeaderboardSort.Wins => "WINS",
            LeaderboardSort.WinRate => "WIN %",
            _ => "TROPHIES",
        };

        private static string SortColor(LeaderboardSort sort) => sort switch
        {
            LeaderboardSort.Wins => Win,
            LeaderboardSort.WinRate => Info,
            _ => TrophyGold,
        };

        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Initial(string name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

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
