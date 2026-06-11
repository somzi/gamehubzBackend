using System.Text;
using System.Text.Encodings.Web;

namespace GameHubz.Api.Share
{
    public record SharePageModel
    {
        public required string Title { get; init; }

        public required string Description { get; init; }

        public required string CanonicalUrl { get; init; }

        public required string DeepLink { get; init; }

        public required string EntityLabel { get; init; }

        public string? ImageUrl { get; init; }

        public string AppName { get; init; } = "GameHubz";

        public string? AppStoreUrl { get; init; }

        public string? PlayStoreUrl { get; init; }
    }

    /// <summary>
    /// Renders the public share page: Open Graph / Twitter meta tags for link-preview
    /// crawlers, plus a small fallback UI that tries to deep-link into the app and
    /// offers store links when the app is not installed.
    /// </summary>
    public static class SharePageBuilder
    {
        public static string BuildPage(SharePageModel model)
        {
            string title = H(model.Title);
            string fullTitle = H($"{model.Title} | {model.AppName}");
            string description = H(model.Description);
            string url = H(model.CanonicalUrl);
            string deepLink = H(model.DeepLink);
            string appName = H(model.AppName);

            var imageTags = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.ImageUrl))
            {
                imageTags.AppendLine($"    <meta property=\"og:image\" content=\"{H(model.ImageUrl)}\" />");
                imageTags.AppendLine($"    <meta name=\"twitter:image\" content=\"{H(model.ImageUrl)}\" />");
            }

            string avatar = !string.IsNullOrWhiteSpace(model.ImageUrl)
                ? $"<img class=\"avatar\" src=\"{H(model.ImageUrl)}\" alt=\"\" />"
                : $"<div class=\"avatar avatar-fallback\">{H(model.Title.Length > 0 ? model.Title[..1].ToUpperInvariant() : "G")}</div>";

            var storeButtons = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.AppStoreUrl))
            {
                storeButtons.Append($"<a href=\"{H(model.AppStoreUrl)}\">App Store</a>");
            }

            if (!string.IsNullOrWhiteSpace(model.PlayStoreUrl))
            {
                storeButtons.Append($"<a href=\"{H(model.PlayStoreUrl)}\">Google Play</a>");
            }

            string storesSection = storeButtons.Length > 0
                ? $"<div class=\"stores\">{storeButtons}</div>\n        <p class=\"hint\">Don't have the app yet? Get it above.</p>"
                : "";

            return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{fullTitle}}</title>
    <meta name="description" content="{{description}}" />
    <link rel="canonical" href="{{url}}" />
    <meta property="og:site_name" content="{{appName}}" />
    <meta property="og:type" content="website" />
    <meta property="og:title" content="{{title}}" />
    <meta property="og:description" content="{{description}}" />
    <meta property="og:url" content="{{url}}" />
{{imageTags}}    <meta name="twitter:card" content="summary" />
    <meta name="twitter:title" content="{{title}}" />
    <meta name="twitter:description" content="{{description}}" />
    <meta name="theme-color" content="#0b0e14" />
    <style>
        :root { color-scheme: dark; }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: #0b0e14; color: #eef1f6; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; padding: 24px; }
        .card { width: 100%; max-width: 420px; background: #141a24; border: 1px solid #232c3b; border-radius: 20px; padding: 32px 28px; text-align: center; }
        .avatar { width: 96px; height: 96px; border-radius: 24px; object-fit: cover; margin: 0 auto 16px; display: block; background: #232c3b; }
        .avatar-fallback { display: flex; align-items: center; justify-content: center; font-size: 40px; font-weight: 700; background: linear-gradient(135deg, #7c5cff, #46c8ff); color: #fff; }
        .chip { display: inline-block; font-size: 11px; letter-spacing: .12em; text-transform: uppercase; color: #9aa7bd; border: 1px solid #232c3b; border-radius: 999px; padding: 4px 10px; margin-bottom: 12px; }
        h1 { font-size: 24px; margin-bottom: 8px; word-break: break-word; }
        .desc { color: #9aa7bd; font-size: 14px; line-height: 1.5; margin-bottom: 24px; word-break: break-word; }
        .btn { display: block; width: 100%; border-radius: 12px; padding: 14px 16px; font-size: 15px; font-weight: 600; text-decoration: none; cursor: pointer; margin-bottom: 10px; border: 1px solid transparent; font-family: inherit; }
        .primary { background: linear-gradient(135deg, #7c5cff, #5a8bff); color: #fff; }
        .ghost { background: transparent; border-color: #232c3b; color: #eef1f6; }
        .stores { display: flex; gap: 10px; justify-content: center; margin-top: 14px; }
        .stores a { color: #9aa7bd; font-size: 13px; text-decoration: none; border: 1px solid #232c3b; border-radius: 10px; padding: 10px 14px; flex: 1; }
        .hint { color: #5d6b82; font-size: 12px; margin-top: 16px; }
    </style>
</head>
<body>
    <main class="card">
        {{avatar}}
        <span class="chip">{{H(model.EntityLabel)}}</span>
        <h1>{{title}}</h1>
        <p class="desc">{{description}}</p>
        <a class="btn primary" href="{{deepLink}}">Open in {{appName}}</a>
        <button class="btn ghost" id="copyLink" type="button">Copy link</button>
        {{storesSection}}
    </main>
    <script>
        (function () {
            var deepLink = '{{J(model.DeepLink)}}';
            var pageUrl = '{{J(model.CanonicalUrl)}}';
            var isMobile = /android|iphone|ipad|ipod/i.test(navigator.userAgent || '');

            // If the app is installed the OS takes over; otherwise nothing happens
            // and this fallback page simply stays visible.
            if (isMobile) {
                setTimeout(function () { window.location.href = deepLink; }, 400);
            }

            var copyBtn = document.getElementById('copyLink');
            copyBtn.addEventListener('click', function () {
                function done() { copyBtn.textContent = 'Link copied!'; }
                function legacyCopy() {
                    var input = document.createElement('input');
                    input.value = pageUrl;
                    document.body.appendChild(input);
                    input.select();
                    try { document.execCommand('copy'); done(); } catch (e) { /* ignore */ }
                    document.body.removeChild(input);
                }
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(pageUrl).then(done).catch(legacyCopy);
                } else {
                    legacyCopy();
                }
            });
        })();
    </script>
</body>
</html>
""";
        }

        private static string H(string? value)
        {
            return HtmlEncoder.Default.Encode(value ?? "");
        }

        private static string J(string? value)
        {
            return JavaScriptEncoder.Default.Encode(value ?? "");
        }
    }
}
