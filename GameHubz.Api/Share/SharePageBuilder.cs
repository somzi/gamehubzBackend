using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace GameHubz.Api.Share
{
    public record ShareStat(string Label, string Value, string? Icon = null, string? Tone = null);

    // Player-profile-only payload that triggers the dedicated scoreboard layout
    // (banner + donut + W/D/L pills). When set, the generic Stats grid is ignored.
    public record PlayerScoreboard(int Matches, int WinRate, int Wins, int Draws, int Losses, int Trophies);

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

        // Optional "by <hub>" row shown under the title (with a small logo).
        public string? ContextText { get; init; }

        public string? ContextImageUrl { get; init; }

        // When present, the page renders these as a stat-tile grid instead of
        // the plain description paragraph (the description still feeds OG tags).
        public IReadOnlyList<ShareStat>? Stats { get; init; }

        // Compact 3-column icon tiles (player profile) vs. 2-column label tiles (tournament).
        public bool CompactStats { get; init; }

        public PlayerScoreboard? Scoreboard { get; init; }
    }

    /// <summary>
    /// Renders the public share page: Open Graph / Twitter meta tags for link-preview
    /// crawlers, plus a polished fallback UI that tries to deep-link into the app and
    /// offers store links when the app is not installed.
    /// </summary>
    public static class SharePageBuilder
    {
        public static string BuildPage(SharePageModel model)
        {
            if (model.Scoreboard is not null)
            {
                return BuildScoreboardPage(model);
            }

            string title = H(model.Title);
            string fullTitle = H($"{model.Title} | {model.AppName}");
            string description = H(model.Description);
            string url = H(model.CanonicalUrl);
            string deepLink = H(model.DeepLink);
            string appName = H(model.AppName);
            string entityLabel = H(model.EntityLabel);
            string entityKey = model.EntityLabel.ToLowerInvariant();

            // Per-entity accent palette: warm gold for tournaments, violet for hubs,
            // cyan for player profiles. Falls back to violet for anything else.
            (string accentA, string accentB, string ringHue) = entityKey switch
            {
                "tournament" => ("#fbbf24", "#f97316", "38 92% 50%"),
                "hub" => ("#a78bfa", "#7c3aed", "262 83% 58%"),
                "player" => ("#22d3ee", "#3b82f6", "199 89% 48%"),
                _ => ("#a78bfa", "#7c3aed", "262 83% 58%"),
            };

            string avatar = !string.IsNullOrWhiteSpace(model.ImageUrl)
                ? $"<img class=\"avatar\" src=\"{H(model.ImageUrl)}\" alt=\"\" />"
                : $"<div class=\"avatar avatar-fallback\">{H(model.Title.Length > 0 ? model.Title[..1].ToUpperInvariant() : "G")}</div>";

            var imageTags = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.ImageUrl))
            {
                imageTags.AppendLine($"    <meta property=\"og:image\" content=\"{H(model.ImageUrl)}\" />");
                imageTags.AppendLine($"    <meta name=\"twitter:image\" content=\"{H(model.ImageUrl)}\" />");
            }

            var storeButtons = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.AppStoreUrl))
            {
                storeButtons.Append($"<a class=\"store-link\" href=\"{H(model.AppStoreUrl)}\"><svg viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"16\" height=\"16\"><path d=\"M17.05 20.28c-.98.95-2.05.88-3.08.43-1.09-.46-2.09-.48-3.24 0-1.44.62-2.2.44-3.06-.42C2.79 15.25 3.51 7.59 9.05 7.31c1.35.07 2.29.74 3.08.8 1.18-.24 2.31-.93 3.57-.84 1.51.12 2.65.72 3.4 1.8-3.12 1.87-2.38 5.98.48 7.13-.57 1.5-1.31 2.99-2.54 4.09zM12.03 7.25c-.15-2.23 1.66-4.07 3.74-4.25.29 2.58-2.34 4.5-3.74 4.25z\"/></svg><span>App Store</span></a>");
            }

            if (!string.IsNullOrWhiteSpace(model.PlayStoreUrl))
            {
                storeButtons.Append($"<a class=\"store-link\" href=\"{H(model.PlayStoreUrl)}\"><svg viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"16\" height=\"16\"><path d=\"M3.6 2.2c-.3.3-.5.8-.5 1.4v16.8c0 .6.2 1.1.5 1.4l9.1-9.8L3.6 2.2zm10.5 9.7l2.6-2.8 5.6 3.2c.8.5.8 1.3 0 1.8l-5.6 3.2-2.6-2.8 0-2.6zm-1 1.1l-9 9.6c.1 0 .3 0 .4-.1l10.7-6.1-2.1-3.4zm0-2.2l2.1-3.4L4.5 1.3c-.1 0-.3-.1-.4-.1l9 9.6z\"/></svg><span>Google Play</span></a>");
            }

            string storesSection = storeButtons.Length > 0
                ? $@"<div class=""footer"">
            <p class=""footer-hint"">Don't have the app yet?</p>
            <div class=""stores"">{storeButtons}</div>
        </div>"
                : "";

            string contextRow = "";
            if (!string.IsNullOrWhiteSpace(model.ContextText))
            {
                string contextLogo = !string.IsNullOrWhiteSpace(model.ContextImageUrl)
                    ? $"<img src=\"{H(model.ContextImageUrl)}\" alt=\"\" />"
                    : "";
                contextRow = $"<div class=\"context\">{contextLogo}<span class=\"context-by\">by</span><span class=\"context-name\">{H(model.ContextText)}</span></div>";
            }

            string detailsSection;
            if (model.Stats is { Count: > 0 })
            {
                var statsBuilder = new StringBuilder($"<div class=\"stats{(model.CompactStats ? " cols-3" : "")}\">");
                foreach (ShareStat stat in model.Stats)
                {
                    string tone = string.IsNullOrWhiteSpace(stat.Tone) ? "" : $" style=\"color:{H(stat.Tone)}\"";

                    if (!string.IsNullOrWhiteSpace(stat.Icon) && StatIcons.TryGetValue(stat.Icon, out string? iconPath))
                    {
                        statsBuilder.Append($"<div class=\"stat stat-c\"{tone}><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" width=\"18\" height=\"18\" aria-hidden=\"true\">{iconPath}</svg><span class=\"stat-value\">{H(stat.Value)}</span><span class=\"stat-label\">{H(stat.Label)}</span></div>");
                    }
                    else
                    {
                        statsBuilder.Append($"<div class=\"stat\"><span class=\"stat-label\">{H(stat.Label)}</span><span class=\"stat-value\">{H(stat.Value)}</span></div>");
                    }
                }

                statsBuilder.Append("</div>");
                detailsSection = statsBuilder.ToString();
            }
            else
            {
                detailsSection = $"<p class=\"desc\">{description}</p>";
            }

            return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <title>{{fullTitle}}</title>
    <meta name="description" content="{{description}}" />
    <link rel="canonical" href="{{url}}" />
    <meta property="og:site_name" content="{{appName}}" />
    <meta property="og:type" content="website" />
    <meta property="og:title" content="{{title}}" />
    <meta property="og:description" content="{{description}}" />
    <meta property="og:url" content="{{url}}" />
{{imageTags}}    <meta name="twitter:card" content="summary_large_image" />
    <meta name="twitter:title" content="{{title}}" />
    <meta name="twitter:description" content="{{description}}" />
    <meta name="theme-color" content="#06070d" />
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=Sora:wght@600;700;800&display=swap">
    <style>
        :root {
            color-scheme: dark;
            --accent-a: {{accentA}};
            --accent-b: {{accentB}};
            --ring-hue: {{ringHue}};
            --bg-0: #06070d;
            --bg-1: #0d1020;
            --surface: rgba(20, 22, 38, 0.55);
            --surface-border: rgba(255, 255, 255, 0.08);
            --surface-border-strong: rgba(255, 255, 255, 0.14);
            --text-0: #f4f6fb;
            --text-1: #aab0c4;
            --text-2: #6b7290;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
        html, body { height: 100%; }
        body {
            min-height: 100vh;
            min-height: 100dvh;
            display: flex;
            align-items: center;
            justify-content: center;
            background: var(--bg-0);
            color: var(--text-0);
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            font-feature-settings: 'cv11', 'ss01', 'ss03';
            -webkit-font-smoothing: antialiased;
            -moz-osx-font-smoothing: grayscale;
            padding: 24px;
            position: relative;
            overflow-x: hidden;
        }
        .aurora { position: fixed; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
        .blob { position: absolute; border-radius: 50%; filter: blur(80px); opacity: .55; will-change: transform; }
        .blob-1 { width: 520px; height: 520px; top: -180px; left: -120px; background: radial-gradient(circle, var(--accent-a), transparent 65%); animation: drift1 18s ease-in-out infinite; }
        .blob-2 { width: 620px; height: 620px; bottom: -220px; right: -160px; background: radial-gradient(circle, var(--accent-b), transparent 65%); animation: drift2 22s ease-in-out infinite; }
        .blob-3 { width: 380px; height: 380px; top: 40%; left: 50%; transform: translate(-50%, -50%); background: radial-gradient(circle, rgba(99, 102, 241, 0.35), transparent 70%); animation: drift3 26s ease-in-out infinite; }
        @keyframes drift1 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(40px, 30px) scale(1.08); } }
        @keyframes drift2 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(-30px, -40px) scale(1.12); } }
        @keyframes drift3 { 0%,100% { transform: translate(-50%, -50%) scale(1); } 50% { transform: translate(-45%, -55%) scale(1.15); } }
        .grain { position: fixed; inset: 0; z-index: 1; pointer-events: none; opacity: .04; mix-blend-mode: overlay; background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>"); }

        .card {
            position: relative;
            z-index: 2;
            width: 100%;
            max-width: 440px;
            padding: 36px 28px 28px;
            text-align: center;
            background: linear-gradient(180deg, rgba(28, 30, 50, 0.78), rgba(14, 16, 30, 0.78));
            border: 1px solid var(--surface-border);
            border-radius: 28px;
            backdrop-filter: blur(24px) saturate(140%);
            -webkit-backdrop-filter: blur(24px) saturate(140%);
            box-shadow:
                0 30px 80px -20px rgba(0, 0, 0, 0.6),
                0 0 0 1px rgba(255, 255, 255, 0.04) inset,
                0 1px 0 0 rgba(255, 255, 255, 0.08) inset;
        }
        .card::before {
            content: "";
            position: absolute;
            inset: -1px;
            border-radius: 28px;
            padding: 1px;
            background: linear-gradient(140deg, hsla(var(--ring-hue), 90%, 60%, 0.35), transparent 40%, hsla(var(--ring-hue), 90%, 60%, 0.15));
            -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
            mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
            -webkit-mask-composite: xor;
            mask-composite: exclude;
            pointer-events: none;
        }

        .avatar-wrap { position: relative; width: 104px; height: 104px; margin: 0 auto 18px; }
        .avatar-glow {
            position: absolute; inset: -14px;
            border-radius: 50%;
            background: radial-gradient(circle, hsla(var(--ring-hue), 90%, 60%, 0.55), transparent 65%);
            filter: blur(14px);
            animation: breathe 4s ease-in-out infinite;
        }
        @keyframes breathe { 0%,100% { opacity: .55; transform: scale(1); } 50% { opacity: .85; transform: scale(1.08); } }
        .avatar {
            position: relative;
            width: 104px; height: 104px;
            border-radius: 28px;
            object-fit: cover;
            background: #1a1d2e;
            border: 1px solid rgba(255, 255, 255, 0.1);
            box-shadow: 0 12px 32px -8px rgba(0,0,0,0.55), 0 0 0 1px rgba(0,0,0,0.4) inset;
        }
        .avatar-fallback {
            display: flex; align-items: center; justify-content: center;
            font-family: 'Sora', 'Inter', sans-serif;
            font-size: 42px; font-weight: 800;
            background: linear-gradient(135deg, var(--accent-a), var(--accent-b));
            color: #0a0b14;
            letter-spacing: -0.02em;
        }

        .chip {
            display: inline-flex; align-items: center; gap: 6px;
            font-size: 10px;
            letter-spacing: 0.18em;
            text-transform: uppercase;
            font-weight: 700;
            color: hsl(var(--ring-hue), 100%, 78%);
            background: hsla(var(--ring-hue), 90%, 50%, 0.12);
            border: 1px solid hsla(var(--ring-hue), 90%, 60%, 0.28);
            padding: 6px 12px;
            border-radius: 999px;
            margin-bottom: 14px;
        }
        .chip::before {
            content: "";
            width: 5px; height: 5px;
            border-radius: 50%;
            background: hsl(var(--ring-hue), 90%, 60%);
            box-shadow: 0 0 8px hsla(var(--ring-hue), 90%, 60%, 0.8);
        }

        h1 {
            font-family: 'Sora', 'Inter', sans-serif;
            font-size: 30px;
            font-weight: 800;
            letter-spacing: -0.025em;
            line-height: 1.15;
            margin-bottom: 10px;
            word-break: break-word;
            background: linear-gradient(180deg, #ffffff, #c9cee0);
            -webkit-background-clip: text;
            background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .desc {
            color: var(--text-1);
            font-size: 14px;
            line-height: 1.55;
            margin-bottom: 26px;
            word-break: break-word;
        }

        .context {
            display: inline-flex;
            align-items: center;
            gap: 7px;
            margin-bottom: 18px;
            padding: 6px 14px 6px 8px;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--surface-border);
            border-radius: 999px;
            max-width: 100%;
        }
        .context img {
            width: 22px; height: 22px;
            border-radius: 7px;
            object-fit: cover;
            background: #1a1d2e;
        }
        .context-by { font-size: 12px; color: var(--text-2); }
        .context-name {
            font-size: 13px;
            font-weight: 600;
            color: var(--text-0);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .stats {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px;
            margin-bottom: 26px;
            text-align: left;
        }
        .stat {
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--surface-border);
            border-radius: 14px;
            padding: 12px 14px;
            transition: border-color .2s ease, background .2s ease;
        }
        .stat:hover { background: rgba(255, 255, 255, 0.05); border-color: var(--surface-border-strong); }
        .stats:not(.cols-3) .stat:last-child:nth-child(odd) { grid-column: span 2; }
        .stats.cols-3 { grid-template-columns: repeat(3, 1fr); gap: 8px; }
        .stat-c {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 3px;
            text-align: center;
            padding: 13px 6px 11px;
            color: var(--text-0);
        }
        .stat-c svg { opacity: .9; margin-bottom: 2px; }
        .stat-c .stat-value {
            font-family: 'Sora', 'Inter', sans-serif;
            font-size: 17px;
            font-weight: 700;
            color: inherit;
            letter-spacing: -0.01em;
        }
        .stat-c .stat-label {
            margin-bottom: 0;
            font-size: 9px;
            color: var(--text-2);
        }
        .stat-label {
            display: block;
            font-size: 10px;
            letter-spacing: 0.14em;
            text-transform: uppercase;
            font-weight: 700;
            color: var(--text-2);
            margin-bottom: 4px;
        }
        .stat-value {
            display: block;
            font-size: 14px;
            font-weight: 600;
            color: var(--text-0);
            line-height: 1.3;
            word-break: break-word;
        }

        .btn {
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            width: 100%;
            border-radius: 14px;
            padding: 14px 18px;
            font-family: inherit;
            font-size: 15px;
            font-weight: 600;
            letter-spacing: -0.01em;
            text-decoration: none;
            cursor: pointer;
            border: 1px solid transparent;
            margin-bottom: 10px;
            transition: transform .15s ease, box-shadow .25s ease, background .25s ease, border-color .25s ease;
            position: relative;
            overflow: hidden;
            font-feature-settings: 'cv11';
        }
        .btn:active { transform: translateY(1px); }

        .btn-primary {
            color: #0a0b14;
            background: linear-gradient(135deg, var(--accent-a), var(--accent-b));
            box-shadow: 0 14px 32px -10px hsla(var(--ring-hue), 90%, 55%, 0.55), 0 0 0 1px rgba(255, 255, 255, 0.06) inset;
        }
        .btn-primary::after {
            content: "";
            position: absolute; inset: 0;
            background: linear-gradient(120deg, transparent 30%, rgba(255,255,255,0.35) 50%, transparent 70%);
            transform: translateX(-100%);
            transition: transform .8s ease;
        }
        .btn-primary:hover { box-shadow: 0 18px 44px -10px hsla(var(--ring-hue), 90%, 55%, 0.7), 0 0 0 1px rgba(255, 255, 255, 0.1) inset; }
        .btn-primary:hover::after { transform: translateX(100%); }
        .btn-primary svg { transition: transform .25s ease; }
        .btn-primary:hover svg { transform: translateX(3px); }

        .btn-ghost {
            color: var(--text-0);
            background: rgba(255, 255, 255, 0.04);
            border-color: var(--surface-border-strong);
            backdrop-filter: blur(8px);
        }
        .btn-ghost:hover { background: rgba(255, 255, 255, 0.08); border-color: rgba(255, 255, 255, 0.2); }
        .btn-ghost.copied { color: hsl(142, 71%, 65%); border-color: hsla(142, 71%, 50%, 0.4); background: hsla(142, 71%, 50%, 0.08); }

        .footer { margin-top: 22px; padding-top: 20px; border-top: 1px solid var(--surface-border); }
        .footer-hint { color: var(--text-2); font-size: 12px; margin-bottom: 12px; }
        .stores { display: flex; gap: 10px; justify-content: center; }
        .store-link {
            display: inline-flex; align-items: center; gap: 8px;
            color: var(--text-1);
            font-size: 13px;
            font-weight: 500;
            text-decoration: none;
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--surface-border);
            border-radius: 10px;
            padding: 10px 14px;
            flex: 1;
            justify-content: center;
            transition: color .2s ease, border-color .2s ease, background .2s ease;
        }
        .store-link:hover { color: var(--text-0); border-color: var(--surface-border-strong); background: rgba(255, 255, 255, 0.06); }
        .store-link svg { opacity: .8; }

        .brand {
            position: fixed;
            bottom: 24px;
            left: 0; right: 0;
            text-align: center;
            font-family: 'Sora', sans-serif;
            font-size: 11px;
            font-weight: 700;
            letter-spacing: 0.22em;
            text-transform: uppercase;
            color: var(--text-2);
            z-index: 2;
            pointer-events: none;
        }
        .brand span { background: linear-gradient(135deg, var(--accent-a), var(--accent-b)); -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent; }

        @media (max-width: 380px) {
            .card { padding: 30px 22px 22px; border-radius: 24px; }
            h1 { font-size: 26px; }
            .avatar, .avatar-wrap { width: 92px; height: 92px; }
        }
        @media (prefers-reduced-motion: reduce) {
            .blob, .avatar-glow, .btn-primary::after, .btn-primary svg { animation: none !important; transition: none !important; }
        }
    </style>
</head>
<body>
    <div class="aurora" aria-hidden="true">
        <div class="blob blob-1"></div>
        <div class="blob blob-2"></div>
        <div class="blob blob-3"></div>
    </div>
    <div class="grain" aria-hidden="true"></div>

    <main class="card">
        <div class="avatar-wrap">
            <div class="avatar-glow" aria-hidden="true"></div>
            {{avatar}}
        </div>
        <span class="chip">{{entityLabel}}</span>
        <h1>{{title}}</h1>
        {{contextRow}}
        {{detailsSection}}

        <a class="btn btn-primary" href="{{deepLink}}">
            <span>Open in {{appName}}</span>
            <svg viewBox="0 0 24 24" fill="none" width="16" height="16" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
        </a>
        <button class="btn btn-ghost" id="copyLink" type="button">
            <svg viewBox="0 0 24 24" fill="none" width="16" height="16" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" id="copyIcon"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
            <span id="copyLabel">Copy link</span>
        </button>

        {{storesSection}}
    </main>

    <footer class="brand"><span>{{appName}}</span></footer>

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
            var copyLabel = document.getElementById('copyLabel');
            var copyIcon = document.getElementById('copyIcon');
            var resetTimer;

            function flashCopied() {
                copyBtn.classList.add('copied');
                copyLabel.textContent = 'Link copied!';
                copyIcon.innerHTML = '<polyline points="20 6 9 17 4 12"></polyline>';
                clearTimeout(resetTimer);
                resetTimer = setTimeout(function () {
                    copyBtn.classList.remove('copied');
                    copyLabel.textContent = 'Copy link';
                    copyIcon.innerHTML = '<rect x="9" y="9" width="13" height="13" rx="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>';
                }, 2200);
            }

            function legacyCopy() {
                var input = document.createElement('input');
                input.value = pageUrl;
                document.body.appendChild(input);
                input.select();
                try { document.execCommand('copy'); flashCopied(); } catch (e) { /* ignore */ }
                document.body.removeChild(input);
            }

            copyBtn.addEventListener('click', function () {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(pageUrl).then(flashCopied).catch(legacyCopy);
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

        private static string BuildScoreboardPage(SharePageModel model)
        {
            PlayerScoreboard sb = model.Scoreboard!;
            string title = H(model.Title);
            string fullTitle = H($"{model.Title} | {model.AppName}");
            string description = H(model.Description);
            string url = H(model.CanonicalUrl);
            string deepLink = H(model.DeepLink);
            string appName = H(model.AppName);
            string entityLabel = H(model.EntityLabel);

            string avatar = !string.IsNullOrWhiteSpace(model.ImageUrl)
                ? $"<img src=\"{H(model.ImageUrl)}\" alt=\"\" />"
                : $"<div class=\"avatar-fallback\">{H(model.Title.Length > 0 ? model.Title[..1].ToUpperInvariant() : "G")}</div>";

            var imageTags = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.ImageUrl))
            {
                imageTags.AppendLine($"    <meta property=\"og:image\" content=\"{H(model.ImageUrl)}\" />");
                imageTags.AppendLine($"    <meta name=\"twitter:image\" content=\"{H(model.ImageUrl)}\" />");
            }

            var storeButtons = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(model.AppStoreUrl))
            {
                storeButtons.Append($"<a class=\"store-link\" href=\"{H(model.AppStoreUrl)}\"><svg viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"16\" height=\"16\"><path d=\"M17.05 20.28c-.98.95-2.05.88-3.08.43-1.09-.46-2.09-.48-3.24 0-1.44.62-2.2.44-3.06-.42C2.79 15.25 3.51 7.59 9.05 7.31c1.35.07 2.29.74 3.08.8 1.18-.24 2.31-.93 3.57-.84 1.51.12 2.65.72 3.4 1.8-3.12 1.87-2.38 5.98.48 7.13-.57 1.5-1.31 2.99-2.54 4.09zM12.03 7.25c-.15-2.23 1.66-4.07 3.74-4.25.29 2.58-2.34 4.5-3.74 4.25z\"/></svg><span>App Store</span></a>");
            }

            if (!string.IsNullOrWhiteSpace(model.PlayStoreUrl))
            {
                storeButtons.Append($"<a class=\"store-link\" href=\"{H(model.PlayStoreUrl)}\"><svg viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"16\" height=\"16\"><path d=\"M3.6 2.2c-.3.3-.5.8-.5 1.4v16.8c0 .6.2 1.1.5 1.4l9.1-9.8L3.6 2.2zm10.5 9.7l2.6-2.8 5.6 3.2c.8.5.8 1.3 0 1.8l-5.6 3.2-2.6-2.8 0-2.6zm-1 1.1l-9 9.6c.1 0 .3 0 .4-.1l10.7-6.1-2.1-3.4zm0-2.2l2.1-3.4L4.5 1.3c-.1 0-.3-.1-.4-.1l9 9.6z\"/></svg><span>Google Play</span></a>");
            }

            string storesSection = storeButtons.Length > 0
                ? $@"<div class=""footer"">
                <p class=""footer-hint"">Don't have the app yet?</p>
                <div class=""stores"">{storeButtons}</div>
            </div>"
                : "";

            // Donut: circumference = 2 * PI * r (r = 52). Dasharray fills `winRate %` of the ring.
            const double radius = 52;
            double circumference = 2 * Math.PI * radius;
            int winRate = Math.Clamp(sb.WinRate, 0, 100);
            double filled = winRate * circumference / 100.0;
            string dashArray = $"{filled.ToString("0.00", CultureInfo.InvariantCulture)} {circumference.ToString("0.00", CultureInfo.InvariantCulture)}";

            return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <title>{{fullTitle}}</title>
    <meta name="description" content="{{description}}" />
    <link rel="canonical" href="{{url}}" />
    <meta property="og:site_name" content="{{appName}}" />
    <meta property="og:type" content="website" />
    <meta property="og:title" content="{{title}}" />
    <meta property="og:description" content="{{description}}" />
    <meta property="og:url" content="{{url}}" />
{{imageTags}}    <meta name="twitter:card" content="summary_large_image" />
    <meta name="twitter:title" content="{{title}}" />
    <meta name="twitter:description" content="{{description}}" />
    <meta name="theme-color" content="#06070d" />
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap">
    <style>
        :root { color-scheme: dark; }
        * { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
        html, body { height: 100%; }
        body {
            min-height: 100vh;
            min-height: 100dvh;
            display: flex;
            align-items: center;
            justify-content: center;
            background: radial-gradient(ellipse at top, #0d1020 0%, #06070d 70%);
            color: #f8fafc;
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            -webkit-font-smoothing: antialiased;
            -moz-osx-font-smoothing: grayscale;
            padding: 24px;
            position: relative;
            overflow-x: hidden;
        }

        .aurora { position: fixed; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
        .blob { position: absolute; border-radius: 50%; filter: blur(80px); opacity: .55; will-change: transform; }
        .blob-1 { width: 520px; height: 520px; top: -180px; left: -120px; background: radial-gradient(circle, #22d3ee, transparent 65%); animation: drift1 18s ease-in-out infinite; }
        .blob-2 { width: 620px; height: 620px; bottom: -220px; right: -160px; background: radial-gradient(circle, #3b82f6, transparent 65%); animation: drift2 22s ease-in-out infinite; }
        .blob-3 { width: 380px; height: 380px; top: 40%; left: 50%; transform: translate(-50%, -50%); background: radial-gradient(circle, rgba(99, 102, 241, 0.35), transparent 70%); animation: drift3 26s ease-in-out infinite; }
        @keyframes drift1 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(40px, 30px) scale(1.08); } }
        @keyframes drift2 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(-30px, -40px) scale(1.12); } }
        @keyframes drift3 { 0%,100% { transform: translate(-50%, -50%) scale(1); } 50% { transform: translate(-45%, -55%) scale(1.15); } }
        .grain { position: fixed; inset: 0; z-index: 1; pointer-events: none; opacity: .04; mix-blend-mode: overlay; background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>"); }

        .card {
            position: relative;
            z-index: 2;
            width: 100%;
            max-width: 400px;
            border-radius: 28px;
            background: #0b111d;
            border: 1px solid rgba(148, 163, 184, 0.12);
            overflow: hidden;
            box-shadow: 0 32px 64px -24px rgba(0, 0, 0, 0.7);
        }

        .banner {
            height: 96px;
            background: linear-gradient(120deg, #0e7490 0%, #2563eb 60%, #1e3a8a 100%);
            position: relative;
        }
        .banner::after {
            content: "";
            position: absolute;
            inset: 0;
            background: radial-gradient(circle at 80% 0%, rgba(255, 255, 255, 0.18), transparent 55%);
        }

        .content { padding: 0 28px 28px; margin-top: -44px; position: relative; }

        .avatar-wrap {
            width: 88px; height: 88px;
            border-radius: 26px;
            padding: 4px;
            background: #0b111d;
            margin: 0 auto;
        }
        .avatar-wrap img,
        .avatar-wrap .avatar-fallback {
            width: 100%; height: 100%;
            border-radius: 22px;
            object-fit: cover;
            display: block;
        }
        .avatar-fallback {
            display: flex; align-items: center; justify-content: center;
            font-size: 36px; font-weight: 800;
            background: linear-gradient(135deg, #2dd4ed, #3b82f6);
            color: #06121c;
            letter-spacing: -0.02em;
        }

        .name {
            margin-top: 12px;
            font-size: 30px;
            font-weight: 700;
            color: #f8fafc;
            letter-spacing: -0.02em;
            text-align: center;
            word-break: break-word;
            line-height: 1.15;
        }

        .role {
            margin: 6px auto 0;
            display: inline-flex;
            padding: 4px 12px;
            border-radius: 99px;
            background: rgba(45, 212, 237, 0.12);
            border: 1px solid rgba(45, 212, 237, 0.3);
            font-size: 10px;
            font-weight: 700;
            letter-spacing: 0.18em;
            color: #2dd4ed;
            text-transform: uppercase;
        }
        .role-wrap { display: flex; justify-content: center; }

        .stats-row {
            margin-top: 24px;
            display: grid;
            grid-template-columns: 1fr 124px 1fr;
            align-items: center;
            gap: 8px;
        }
        .side-stat {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 2px;
        }
        .side-stat .v {
            font-size: 26px;
            font-weight: 700;
            color: #f8fafc;
            line-height: 1;
        }
        .side-stat.trophies .v { color: #fbbf24; }
        .side-stat .l {
            font-size: 10px;
            font-weight: 600;
            letter-spacing: 0.12em;
            color: #64748b;
            text-transform: uppercase;
        }

        .donut { position: relative; width: 124px; height: 124px; margin: 0 auto; }
        .donut svg { transform: rotate(-90deg); }
        .donut-center {
            position: absolute;
            inset: 0;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
        }
        .donut-center .rate {
            font-size: 28px;
            font-weight: 700;
            color: #f8fafc;
            line-height: 1;
        }
        .donut-center .rate-label {
            margin-top: 4px;
            font-size: 9px;
            font-weight: 600;
            letter-spacing: 0.14em;
            color: #64748b;
            text-transform: uppercase;
        }

        .wdl {
            margin-top: 22px;
            display: grid;
            grid-template-columns: 1fr 1fr 1fr;
            gap: 10px;
        }
        .pill {
            border-radius: 14px;
            padding: 12px 0;
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 2px;
        }
        .pill .v { font-size: 20px; font-weight: 700; line-height: 1; }
        .pill .l {
            font-size: 10px;
            font-weight: 600;
            letter-spacing: 0.12em;
            text-transform: uppercase;
        }
        .pill.win { background: rgba(52, 211, 153, 0.08); border: 1px solid rgba(52, 211, 153, 0.22); }
        .pill.win .v { color: #34d399; }
        .pill.win .l { color: rgba(52, 211, 153, 0.7); }
        .pill.draw { background: rgba(148, 163, 184, 0.06); border: 1px solid rgba(148, 163, 184, 0.18); }
        .pill.draw .v { color: #94a3b8; }
        .pill.draw .l { color: #64748b; }
        .pill.loss { background: rgba(248, 113, 113, 0.07); border: 1px solid rgba(248, 113, 113, 0.22); }
        .pill.loss .v { color: #f87171; }
        .pill.loss .l { color: rgba(248, 113, 113, 0.7); }

        .actions { margin-top: 24px; display: flex; gap: 10px; }
        .open-btn {
            flex: 1;
            height: 50px;
            border-radius: 14px;
            background: linear-gradient(90deg, #2dd4ed, #3b82f6);
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            color: #06121c;
            font-size: 14px;
            font-weight: 700;
            text-decoration: none;
            transition: filter .2s ease, transform .15s ease;
            box-shadow: 0 14px 32px -10px rgba(45, 212, 237, 0.5);
        }
        .open-btn:hover { filter: brightness(1.08); }
        .open-btn:active { transform: translateY(1px); }
        .open-btn .arrow { transition: transform .2s ease; }
        .open-btn:hover .arrow { transform: translateX(3px); }

        .copy-btn {
            width: 50px;
            height: 50px;
            border-radius: 14px;
            border: 1px solid rgba(148, 163, 184, 0.2);
            background: transparent;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #cbd5e1;
            cursor: pointer;
            transition: background .2s ease, border-color .2s ease, color .2s ease;
            font-family: inherit;
        }
        .copy-btn:hover { background: rgba(148, 163, 184, 0.08); border-color: rgba(148, 163, 184, 0.35); color: #f8fafc; }
        .copy-btn.copied { color: #34d399; border-color: rgba(52, 211, 153, 0.4); background: rgba(52, 211, 153, 0.08); }

        .footer { margin-top: 22px; padding-top: 20px; border-top: 1px solid rgba(148, 163, 184, 0.12); }
        .footer-hint { color: #64748b; font-size: 12px; margin-bottom: 12px; text-align: center; }
        .stores { display: flex; gap: 10px; justify-content: center; }
        .store-link {
            display: inline-flex; align-items: center; gap: 8px;
            color: #aab0c4;
            font-size: 13px;
            font-weight: 500;
            text-decoration: none;
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid rgba(148, 163, 184, 0.18);
            border-radius: 10px;
            padding: 10px 14px;
            flex: 1;
            justify-content: center;
            transition: color .2s ease, border-color .2s ease, background .2s ease;
        }
        .store-link:hover { color: #f8fafc; border-color: rgba(148, 163, 184, 0.32); background: rgba(255, 255, 255, 0.06); }

        .label-chip {
            margin: 14px 0 0;
            font-size: 12px;
            font-weight: 600;
            letter-spacing: 0.14em;
            color: #94a3b8;
            text-transform: uppercase;
            text-align: center;
        }

        @media (max-width: 380px) {
            .card { border-radius: 24px; }
            .content { padding: 0 22px 22px; }
            .name { font-size: 26px; }
        }
        @media (prefers-reduced-motion: reduce) {
            .blob { animation: none !important; }
        }
    </style>
</head>
<body>
    <div class="aurora" aria-hidden="true">
        <div class="blob blob-1"></div>
        <div class="blob blob-2"></div>
        <div class="blob blob-3"></div>
    </div>
    <div class="grain" aria-hidden="true"></div>

    <main class="card">
        <div class="banner"></div>
        <div class="content">
            <div class="avatar-wrap">{{avatar}}</div>
            <div class="name">{{title}}</div>
            <div class="role-wrap"><div class="role">{{entityLabel}}</div></div>

            <div class="stats-row">
                <div class="side-stat">
                    <div class="v">{{sb.Matches}}</div>
                    <div class="l">Matches</div>
                </div>
                <div class="donut">
                    <svg width="124" height="124" viewBox="0 0 124 124" xmlns="http://www.w3.org/2000/svg">
                        <circle cx="62" cy="62" r="52" fill="none" stroke="rgba(148,163,184,0.14)" stroke-width="10"></circle>
                        <circle cx="62" cy="62" r="52" fill="none" stroke="url(#gh-grad)" stroke-width="10" stroke-linecap="round" stroke-dasharray="{{dashArray}}"></circle>
                        <defs>
                            <linearGradient id="gh-grad" x1="0" y1="0" x2="1" y2="1">
                                <stop offset="0%" stop-color="#2dd4ed"></stop>
                                <stop offset="100%" stop-color="#3b82f6"></stop>
                            </linearGradient>
                        </defs>
                    </svg>
                    <div class="donut-center">
                        <div class="rate">{{winRate}}%</div>
                        <div class="rate-label">Win rate</div>
                    </div>
                </div>
                <div class="side-stat trophies">
                    <div class="v">{{sb.Trophies}}</div>
                    <div class="l">Trophies</div>
                </div>
            </div>

            <div class="wdl">
                <div class="pill win"><div class="v">{{sb.Wins}}</div><div class="l">Wins</div></div>
                <div class="pill draw"><div class="v">{{sb.Draws}}</div><div class="l">Draws</div></div>
                <div class="pill loss"><div class="v">{{sb.Losses}}</div><div class="l">Losses</div></div>
            </div>

            <div class="actions">
                <a class="open-btn" href="{{deepLink}}">
                    <span>Open in {{appName}}</span>
                    <span class="arrow">→</span>
                </a>
                <button class="copy-btn" id="copyLink" type="button" title="Copy link" aria-label="Copy link">
                    <svg id="copyIcon" viewBox="0 0 24 24" fill="none" width="18" height="18" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                </button>
            </div>

            {{storesSection}}
        </div>
    </main>

    <script>
        (function () {
            var deepLink = '{{J(model.DeepLink)}}';
            var pageUrl = '{{J(model.CanonicalUrl)}}';
            var isMobile = /android|iphone|ipad|ipod/i.test(navigator.userAgent || '');

            if (isMobile) {
                setTimeout(function () { window.location.href = deepLink; }, 400);
            }

            var copyBtn = document.getElementById('copyLink');
            var copyIcon = document.getElementById('copyIcon');
            var resetTimer;

            function flashCopied() {
                copyBtn.classList.add('copied');
                copyIcon.innerHTML = '<polyline points="20 6 9 17 4 12"></polyline>';
                clearTimeout(resetTimer);
                resetTimer = setTimeout(function () {
                    copyBtn.classList.remove('copied');
                    copyIcon.innerHTML = '<rect x="9" y="9" width="13" height="13" rx="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>';
                }, 2200);
            }

            function legacyCopy() {
                var input = document.createElement('input');
                input.value = pageUrl;
                document.body.appendChild(input);
                input.select();
                try { document.execCommand('copy'); flashCopied(); } catch (e) { /* ignore */ }
                document.body.removeChild(input);
            }

            copyBtn.addEventListener('click', function () {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(pageUrl).then(flashCopied).catch(legacyCopy);
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

        private static readonly Dictionary<string, string> StatIcons = new()
        {
            ["matches"] = "<path d=\"M22 12h-4l-3 9L9 3l-3 9H2\"/>",
            ["wins"] = "<path d=\"M20 6L9 17l-5-5\"/>",
            ["losses"] = "<path d=\"M18 6L6 18M6 6l12 12\"/>",
            ["draws"] = "<path d=\"M5 9h14M5 15h14\"/>",
            ["winrate"] = "<path d=\"M19 5L5 19\"/><circle cx=\"6.5\" cy=\"6.5\" r=\"2.5\"/><circle cx=\"17.5\" cy=\"17.5\" r=\"2.5\"/>",
            ["trophy"] = "<path d=\"M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22M18 2H6v7a6 6 0 0 0 12 0V2Z\"/>",
        };

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
