using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace GameHubz.Logic.Services
{
    public enum AnnouncementKind
    {
        RegistrationOpened,
        RegistrationClosed,
        TournamentStarted,
        TournamentFinished,
        MatchApproved,
        MatchReverted,
        DoubleWalkover,
    }

    /// <summary>Display data for a hub announcement card. Only the fields relevant to the Kind
    /// are read — everything is pre-resolved so the renderer stays pure.</summary>
    public class AnnouncementCardData
    {
        public AnnouncementKind Kind { get; set; }
        public string HubName { get; set; } = "";
        public string TournamentName { get; set; } = "";

        // RegistrationOpened
        public int MaxPlayers { get; set; }
        public int Prize { get; set; }
        public string PrizeCurrency { get; set; } = "";

        // RegistrationClosed
        public int ParticipantCount { get; set; }

        // TournamentFinished
        public string? ChampionName { get; set; }

        // Match events
        public string HomeName { get; set; } = "";
        public string AwayName { get; set; } = "";
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public bool OpponentAdvances { get; set; }

        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Renders hub webhook announcements (registration opened/closed, tournament started/finished,
    /// match approved/reverted, double walkover) as PNG cards — replacing the plain Discord embeds
    /// of phase 1 with the same premium panel style the slash-command cards use. One renderer for
    /// all events: shared header/footer, event-specific hero block and accent color.
    /// </summary>
    public static class DiscordAnnouncementCard
    {
        private const string Font = "Inter";

        private const string Bg = "#0A0F1E";
        private const string Panel = "#0D1528";
        private const string Cell = "#131C31";
        private const string Stroke = "#22304D";
        private const string TextHi = "#FFFFFF";
        private const string TextLo = "#8CA0C0";
        private const string Accent = "#22D3A6";
        private const string Win = "#34D399";
        private const string Draw = "#FBBF24";
        private const string Info = "#60A5FA";
        private const string Gold = "#FBBF24";
        private const string Orange = "#FB923C";

        public static byte[] Render(AnnouncementCardData d)
        {
            var (label, color, icon) = Style(d.Kind);

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

                                // ── Header: event badge + tournament name ──
                                panel.Item().Row(row =>
                                {
                                    row.ConstantItem(42).Height(42).CornerRadius(21).Background(Cell)
                                        .Border(1.5f).BorderColor(color)
                                        .AlignMiddle().AlignCenter().Width(20).Height(20).Svg(icon);

                                    row.RelativeItem().PaddingLeft(13).AlignMiddle().Column(c =>
                                    {
                                        c.Item().Text(label).FontSize(9.5f).Bold().FontColor(color).LetterSpacing(0.18f);
                                        c.Item().PaddingTop(3).Text(d.TournamentName).FontSize(19).Bold().FontColor(TextHi).ClampLines(1, "…");
                                    });
                                });

                                panel.Item().Height(2).CornerRadius(1).Background(Stroke);

                                switch (d.Kind)
                                {
                                    case AnnouncementKind.RegistrationOpened: RegistrationOpened(panel, d); break;
                                    case AnnouncementKind.RegistrationClosed: RegistrationClosed(panel, d); break;
                                    case AnnouncementKind.TournamentStarted: TournamentStarted(panel); break;
                                    case AnnouncementKind.TournamentFinished: TournamentFinished(panel, d); break;
                                    case AnnouncementKind.MatchApproved: MatchHero(panel, d, Win, struckScore: false, "Final score"); break;
                                    case AnnouncementKind.MatchReverted: MatchHero(panel, d, Orange, struckScore: true, "The match is open again"); break;
                                    case AnnouncementKind.DoubleWalkover: DoubleWalkover(panel, d); break;
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

        // ── Event bodies ──

        private static void RegistrationOpened(ColumnDescriptor panel, AnnouncementCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(18).PaddingHorizontal(14).Column(c =>
                {
                    c.Item().AlignCenter().Text("Registration is open — grab your spot!")
                        .FontSize(13.5f).Bold().FontColor(TextHi);

                    bool hasSlots = d.MaxPlayers > 0;
                    bool hasPrize = d.Prize > 0;
                    if (hasSlots || hasPrize)
                        c.Item().PaddingTop(11).AlignCenter().Row(chips =>
                        {
                            if (hasSlots)
                                Chip(chips, PeopleIcon(Accent), $"{d.MaxPlayers} SLOTS", Accent);
                            if (hasSlots && hasPrize)
                                chips.ConstantItem(8);
                            if (hasPrize)
                                Chip(chips, TrophyIcon(Gold), $"PRIZE {d.Prize} {d.PrizeCurrency}", Gold);
                        });
                });
        }

        private static void RegistrationClosed(ColumnDescriptor panel, AnnouncementCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(16).Column(c =>
                {
                    c.Item().AlignCenter().Text(d.ParticipantCount.ToString()).FontSize(30).Bold().FontColor(Draw);
                    c.Item().PaddingTop(3).AlignCenter()
                        .Text("PARTICIPANTS LOCKED IN").FontSize(8.5f).Bold().FontColor(TextLo).LetterSpacing(0.16f);
                    c.Item().PaddingTop(7).AlignCenter()
                        .Text("Registration has closed — the draw is next.").FontSize(10).FontColor(TextLo);
                });
        }

        private static void TournamentStarted(ColumnDescriptor panel)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(18).Column(c =>
                {
                    c.Item().AlignCenter().Width(28).Height(28).Svg(BoltIcon(Info));
                    c.Item().PaddingTop(9).AlignCenter()
                        .Text("The bracket is drawn — play begins!").FontSize(13.5f).Bold().FontColor(TextHi);
                    c.Item().PaddingTop(4).AlignCenter()
                        .Text("GOOD LUCK TO ALL PLAYERS").FontSize(8).Bold().FontColor(TextLo).LetterSpacing(0.16f);
                });
        }

        private static void TournamentFinished(ColumnDescriptor panel, AnnouncementCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(20).Column(c =>
                {
                    c.Item().AlignCenter().Width(34).Height(34).Svg(TrophyIcon(Gold));

                    if (!string.IsNullOrWhiteSpace(d.ChampionName))
                    {
                        c.Item().PaddingTop(10).AlignCenter()
                            .Text("CHAMPION").FontSize(9).Bold().FontColor(Gold).LetterSpacing(0.2f);
                        c.Item().PaddingTop(4).AlignCenter()
                            .Text(d.ChampionName).FontSize(24).Bold().FontColor(Gold).ClampLines(1, "…");
                        c.Item().PaddingTop(5).AlignCenter()
                            .Text("Congratulations!").FontSize(10).FontColor(TextLo);
                    }
                    else
                        c.Item().PaddingTop(10).AlignCenter()
                            .Text("The tournament has finished.").FontSize(13.5f).Bold().FontColor(TextHi);
                });
        }

        // Shared hero for MatchApproved / MatchReverted: names around a centered score. Reverted
        // strikes the removed score through and follows up with the "open again" note.
        private static void MatchHero(ColumnDescriptor panel, AnnouncementCardData d, string color, bool struckScore, string caption)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(16).PaddingHorizontal(12).Column(c =>
                {
                    c.Item().Row(row =>
                    {
                        row.RelativeItem().AlignMiddle().AlignRight()
                            .Text(d.HomeName).FontSize(13.5f).Bold().FontColor(TextHi).ClampLines(1, "…");

                        row.ConstantItem(96).AlignMiddle().AlignCenter().Text(t =>
                        {
                            string score = d.HomeScore.HasValue && d.AwayScore.HasValue
                                ? $"{d.HomeScore} – {d.AwayScore}"
                                : "vs";
                            var span = t.Span(score).FontSize(24).Bold()
                                .FontColor(struckScore ? TextLo : color);
                            if (struckScore && d.HomeScore.HasValue)
                                span.Strikethrough();
                        });

                        row.RelativeItem().AlignMiddle()
                            .Text(d.AwayName).FontSize(13.5f).Bold().FontColor(TextHi).ClampLines(1, "…");
                    });

                    c.Item().PaddingTop(8).AlignCenter()
                        .Text(caption.ToUpperInvariant()).FontSize(8.5f).Bold().FontColor(color).LetterSpacing(0.14f);
                });
        }

        private static void DoubleWalkover(ColumnDescriptor panel, AnnouncementCardData d)
        {
            panel.Item().CornerRadius(16).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(16).Column(c =>
                {
                    c.Item().AlignCenter().Width(26).Height(26).Svg(BanIcon(Orange));
                    c.Item().PaddingTop(8).AlignCenter()
                        .Text($"{d.HomeName} vs {d.AwayName}").FontSize(14).Bold().FontColor(TextHi).ClampLines(1, "…");
                    c.Item().PaddingTop(5).AlignCenter()
                        .Text(d.OpponentAdvances
                            ? "Neither side showed up — the opponent advances."
                            : "Neither side showed up — no points awarded.")
                        .FontSize(10).FontColor(TextLo);
                });
        }

        private static void Chip(RowDescriptor chips, string icon, string text, string color)
        {
            chips.AutoItem().CornerRadius(9).Background(Bg).Border(1).BorderColor(Stroke)
                .PaddingVertical(5).PaddingHorizontal(9).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(11).Height(11).Svg(icon);
                    r.AutoItem().AlignMiddle().PaddingLeft(5)
                        .Text(text).FontSize(9).Bold().FontColor(color).LetterSpacing(0.08f);
                });
        }

        private static void Footer(ColumnDescriptor panel, AnnouncementCardData d)
        {
            panel.Item().CornerRadius(12).Background(Cell).Border(1).BorderColor(Stroke)
                .PaddingVertical(10).PaddingHorizontal(14).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(13).Height(13).Svg(TrophyIcon(Accent));
                    r.AutoItem().AlignMiddle().PaddingLeft(7).Text("GameHubz").FontSize(10.5f).Bold().FontColor(TextHi);
                    r.AutoItem().AlignMiddle().PaddingLeft(6)
                        .Text("· " + d.HubName).FontSize(9.5f).FontColor(TextLo).ClampLines(1, "…");
                    r.RelativeItem().AlignMiddle().AlignRight()
                        .Text(FormatUtc(d.GeneratedAtUtc, "dd MMM yyyy · HH:mm") + " UTC").FontSize(9).FontColor(TextLo);
                });
        }

        private static (string label, string color, string icon) Style(AnnouncementKind kind) => kind switch
        {
            AnnouncementKind.RegistrationOpened => ("REGISTRATION OPEN", Accent, BellIcon(Accent)),
            AnnouncementKind.RegistrationClosed => ("REGISTRATION CLOSED", Draw, LockIcon(Draw)),
            AnnouncementKind.TournamentStarted => ("TOURNAMENT LIVE", Info, BoltIcon(Info)),
            AnnouncementKind.TournamentFinished => ("TOURNAMENT FINISHED", Gold, TrophyIcon(Gold)),
            AnnouncementKind.MatchApproved => ("RESULT CONFIRMED", Win, CheckIcon(Win)),
            AnnouncementKind.MatchReverted => ("RESULT REMOVED", Orange, UndoIcon(Orange)),
            _ => ("DOUBLE WALKOVER", Orange, BanIcon(Orange)),
        };

        private static string FormatUtc(DateTime value, string format)
            => value.ToString(format, CultureInfo.InvariantCulture);

        // ── SVG line icons (Inter has no emoji glyphs, so icons are drawn as vectors) ──

        private static string Svg(string body, string color)
            => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>";

        private static string TrophyIcon(string color)
            => Svg("<path d=\"M8 21h8M12 17v4M7 3h10v5a5 5 0 0 1-10 0V3z\"/><path d=\"M7 5H4c0 3 1.5 5 4 5M17 5h3c0 3-1.5 5-4 5\"/>", color);

        private static string BellIcon(string color)
            => Svg("<path d=\"M6 9a6 6 0 1 1 12 0c0 5 2 6 2 6H4s2-1 2-6\"/><path d=\"M10 20a2.5 2.5 0 0 0 4 0\"/>", color);

        private static string LockIcon(string color)
            => Svg("<rect x=\"5\" y=\"11\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>", color);

        private static string BoltIcon(string color)
            => Svg("<path d=\"M13 2 5 13.5h6L9.5 22 18 10.5h-6L13 2z\"/>", color);

        private static string CheckIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"m8.5 12 2.5 2.5 5-5\"/>", color);

        private static string UndoIcon(string color)
            => Svg("<path d=\"M9 14 4 9l5-5\"/><path d=\"M4 9h10a6 6 0 0 1 0 12h-3\"/>", color);

        private static string BanIcon(string color)
            => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M5.6 5.6l12.8 12.8\"/>", color);

        private static string PeopleIcon(string color)
            => Svg("<circle cx=\"9\" cy=\"8\" r=\"3.5\"/><path d=\"M2.5 20c0-3.5 3-5.5 6.5-5.5s6.5 2 6.5 5.5\"/><path d=\"M16 5a3.5 3.5 0 0 1 0 6.3M18 14.8c2.1.8 3.5 2.4 3.5 5.2\"/>", color);
    }
}
