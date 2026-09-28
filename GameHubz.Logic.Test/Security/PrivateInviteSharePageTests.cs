using GameHubz.Api.Share;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Security;

[TestFixture]
public sealed class PrivateInviteSharePageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void InviteCopyKeepsCode_WhilePublicPreviewUsesCanonicalUrl(bool scoreboardLayout)
    {
        const string publicUrl = "https://share.example/tournament/123";
        const string inviteUrl = publicUrl + "?code=482913";
        var page = SharePageBuilder.BuildPage(new SharePageModel
        {
            Title = "Finals",
            Description = "Tournament details",
            CanonicalUrl = publicUrl,
            CopyUrl = inviteUrl,
            DeepLink = "gamehubz://tournament/123?code=482913",
            EntityLabel = "Tournament",
            Scoreboard = scoreboardLayout ? new PlayerScoreboard(1, 100, 1, 0, 0, 1) : null,
        });

        Assert.That(page, Does.Contain($"<link rel=\"canonical\" href=\"{publicUrl}\" />"));
        Assert.That(page, Does.Contain($"<meta property=\"og:url\" content=\"{publicUrl}\" />"));
        Assert.That(page, Does.Contain($"var pageUrl = '{inviteUrl}';"));
    }
}
