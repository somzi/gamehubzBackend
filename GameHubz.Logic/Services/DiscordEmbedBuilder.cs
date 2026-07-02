using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Builds the Discord webhook payload (embed JSON) for each hub event. Pure presentation —
    /// changing how a message looks happens here and never touches the HTTP send
    /// (<see cref="DiscordNotificationService"/>) or the event routing (the notifiers).
    /// Payloads are anonymous objects serialized camelCase, matching Discord's embed schema.
    /// </summary>
    public class DiscordEmbedBuilder
    {
        // Discord embed accent colors (decimal RGB).
        private const int Green = 0x57F287;   // something opened / a result stands
        private const int Blurple = 0x5865F2; // lifecycle information
        private const int Yellow = 0xFEE75C;  // something is closing / locked in
        private const int Orange = 0xE67E22;  // admin corrections
        private const int Gold = 0xF1C40F;    // champions

        // The embed "title" field is a fixed size — it can't be made any bigger. Discord's markdown
        // heading syntax ("# text") inside the DESCRIPTION renders noticeably larger than the title
        // bar, so the tournament name goes there as an H1 and title is dropped entirely to avoid
        // showing the name twice.
        public object RegistrationOpened(string hubName, string tournamentName, int maxPlayers, int prize, PrizeCurrency prizeCurrency)
            => BuildEmbed(
                null,
                BuildRegistrationOpenedDescription(tournamentName, maxPlayers, prize, prizeCurrency),
                Green,
                hubName);

        private static string BuildRegistrationOpenedDescription(string tournamentName, int maxPlayers, int prize, PrizeCurrency prizeCurrency)
        {
            var lines = new List<string> { $"# 🏆 {tournamentName}" };
            if (maxPlayers > 0) lines.Add($"👥 **{maxPlayers}** slots");
            if (prize > 0) lines.Add($"💰 Prize pool: **{prize} {PrizeCurrencyLabel(prizeCurrency)}**");
            lines.Add("📣 **Registration is open — grab your spot!**");
            return string.Join("\n", lines);
        }

        // Mirrors the labels shown in the mobile Create Tournament form (prizeCurrencies in
        // CreateTournamentModal.tsx) so the Discord announcement matches what the organizer picked.
        private static string PrizeCurrencyLabel(PrizeCurrency currency) => currency switch
        {
            PrizeCurrency.Eur => "EUR",
            PrizeCurrency.Dollar => "USD",
            PrizeCurrency.StarPass => "StarPass",
            PrizeCurrency.FCP => "FCP",
            _ => currency.ToString(),
        };

        public object RegistrationClosed(string hubName, string tournamentName, int participantCount)
            => BuildEmbed(
                $"🔒 Registration Closed — {tournamentName}",
                $"Registration has closed with {participantCount} participants locked in.",
                Yellow,
                hubName);

        public object TournamentStarted(string hubName, string tournamentName)
            => BuildEmbed(
                $"🏁 {tournamentName} is live!",
                "The bracket has been drawn and the tournament has started. Good luck!",
                Blurple,
                hubName);

        public object TournamentFinished(string hubName, string tournamentName, string? championName)
            => BuildEmbed(
                $"🏆 Tournament Finished — {tournamentName}",
                championName == null
                    ? "The tournament has finished."
                    : $"Champion: **{championName}** — congratulations!",
                Gold,
                hubName);

        public object MatchApproved(string hubName, string tournamentName, string homeName, string awayName, int homeScore, int awayScore)
            => BuildEmbed(
                $"✅ Result — {homeName} {homeScore} : {awayScore} {awayName}",
                $"Match result confirmed in **{tournamentName}**.",
                Green,
                hubName);

        public object MatchReverted(string hubName, string tournamentName, string homeName, string awayName, int? homeScore, int? awayScore)
            => BuildEmbed(
                $"↩️ Result Removed — {homeName} vs {awayName}",
                homeScore.HasValue && awayScore.HasValue
                    ? $"The result {homeScore} : {awayScore} was removed in **{tournamentName}**. The match is open again."
                    : $"A result was removed in **{tournamentName}**. The match is open again.",
                Orange,
                hubName);

        public object DoubleWalkover(string hubName, string tournamentName, string homeName, string awayName)
            => BuildEmbed(
                $"🚫 Double Walkover — {homeName} vs {awayName}",
                $"Neither player showed up in **{tournamentName}**. The match was closed with no winner and the opponent advances.",
                Orange,
                hubName);

        // title is nullable: RegistrationOpened puts the tournament name in the description as a
        // markdown heading instead (see above) and omits the title bar entirely.
        private static object BuildEmbed(string? title, string description, int color, string hubName)
            => new
            {
                embeds = new[]
                {
                    new
                    {
                        title,
                        description,
                        color,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        footer = new { text = $"GameHubz • {hubName}" }
                    }
                }
            };
    }
}
