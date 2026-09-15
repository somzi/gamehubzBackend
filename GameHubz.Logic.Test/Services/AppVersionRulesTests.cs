using System;

using NUnit.Framework;

using GameHubz.DataModels.Consts;

namespace GameHubz.Logic.Test.Services
{
    // The app compares this against its own version as plain major.minor.patch and deliberately ignores a
    // value it cannot read — so a typo here would not lock anyone out, it would silently switch the forced
    // update off. This keeps the constant in the one shape the app understands.
    [TestFixture]
    internal sealed class AppVersionRulesTests
    {
        [Test]
        public void MinSupportedAppVersion_IsAPlainThreePartVersion()
        {
            Assert.That(Version.TryParse(AppVersionRules.MinSupportedAppVersion, out _), Is.True);
            Assert.That(AppVersionRules.MinSupportedAppVersion.Split('.'), Has.Length.EqualTo(3));
        }
    }
}
