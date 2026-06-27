namespace GameHubz.Api.Share
{
    /// <summary>
    /// Classifies the User-Agent of a share-page request for ShareLog analytics.
    /// Link-preview crawlers (Discord, WhatsApp, Telegram...) are mapped to their
    /// platform name; regular visitors are mapped to ios / android / web.
    /// </summary>
    public static class ShareBotDetector
    {
        private static readonly (string Token, string Platform)[] BotTokens =
        {
            ("discordbot", "discord"),
            ("whatsapp", "whatsapp"),
            ("telegrambot", "telegram"),
            ("facebookexternalhit", "facebook"),
            ("facebot", "facebook"),
            ("twitterbot", "twitter"),
            ("slackbot", "slack"),
            ("linkedinbot", "linkedin"),
            ("skypeuripreview", "skype"),
            ("viberbot", "viber"),
            ("snapchat", "snapchat"),
            ("pinterestbot", "pinterest"),
            ("redditbot", "reddit"),
            ("vkshare", "vk"),
            ("googlebot", "google"),
            ("bingbot", "bing"),
            ("applebot", "apple"),
            ("embedly", "embedly"),
            ("bitlybot", "bitly"),
        };

        public static bool IsBot(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return false;
            }

            string ua = userAgent.ToLowerInvariant();

            return BotTokens.Any(t => ua.Contains(t.Token))
                || ua.Contains("crawler")
                || ua.Contains("spider")
                || ua.Contains("preview");
        }

        public static string DetectPlatform(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return "unknown";
            }

            string ua = userAgent.ToLowerInvariant();

            foreach ((string token, string platform) in BotTokens)
            {
                if (ua.Contains(token))
                {
                    return platform;
                }
            }

            if (ua.Contains("crawler") || ua.Contains("spider"))
            {
                return "bot";
            }

            // In-app browsers of social apps — a real user tapped the link inside the app.
            if (ua.Contains("instagram"))
            {
                return "instagram";
            }

            if (ua.Contains("fban") || ua.Contains("fbav") || ua.Contains("fb_iab"))
            {
                return "facebook-app";
            }

            if (ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ipod"))
            {
                return "ios";
            }

            if (ua.Contains("android"))
            {
                return "android";
            }

            return "web";
        }
    }
}
