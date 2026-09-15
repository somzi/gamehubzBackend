using GameHubz.DataModels.Consts;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// The language tag reaches us from three places that all spell it differently — the mobile
    /// <c>Language</c> header, the profile column, and appsettings — so the narrowing rule is
    /// what keeps every push and every resource lookup pointed at a set we can actually render.
    /// </summary>
    [TestFixture]
    public class LanguagesTests
    {
        [TestCase("es", "es")]
        [TestCase("ES", "es")]
        [TestCase("  es  ", "es")]
        [TestCase("es-419", "es")]
        [TestCase("es-ES", "es")]
        [TestCase("en-US", "en")]
        [TestCase("pt", "pt")]
        public void Normalize_ReducesTagToBareLowercaseCode(string input, string expected)
        {
            Assert.That(Languages.Normalize(input), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Normalize_ReturnsNullForBlank_SoCallersCanFallBack(string? input)
        {
            // Null is distinct from "unrecognised": callers use it to mean "not specified"
            // and substitute their own default rather than assuming English.
            Assert.That(Languages.Normalize(input), Is.Null);
        }

        [Test]
        public void Normalize_KeepsALeadingDash_RatherThanProducingAnEmptyCode()
        {
            // IndexOf('-') == 0 must not truncate to "", which would then be stored and
            // compared as a real language.
            Assert.That(Languages.Normalize("-es"), Is.EqualTo("-es"));
        }

        [TestCase("es", "es")]
        [TestCase("es-419", "es")]
        [TestCase("ES", "es")]
        [TestCase("en", "en")]
        [TestCase("en-GB", "en")]
        [TestCase("pt", "pt")]
        [TestCase("pt-BR", "pt")]
        [TestCase("pt-PT", "pt")]  // the resource set is written in pt-BR; Portugal reads it too
        [TestCase("PT", "pt")]
        [TestCase("pl", "pl")]
        [TestCase("pl-PL", "pl")]
        [TestCase("sr", "sr")]
        [TestCase("sr-Latn", "sr")]  // the resource set is written in Latin script
        [TestCase("sr-Cyrl", "sr")]
        [TestCase("ru", "ru")]
        [TestCase("ru-RU", "ru")]
        [TestCase("RU", "ru")]
        [TestCase("uk", "uk")]
        [TestCase("uk-UA", "uk")]
        [TestCase("UK", "uk")]
        public void ToSupported_KeepsTheLanguagesWeRender(string input, string expected)
        {
            Assert.That(Languages.ToSupported(input), Is.EqualTo(expected));
        }

        [TestCase("de")]      // plausible next language, not shipped yet
        [TestCase("zzz")]
        [TestCase("")]
        [TestCase(null)]
        public void ToSupported_NarrowsAnythingElseToEnglish(string? input)
        {
            // Storing an unrenderable code on the profile would render English on every push
            // anyway — storing English keeps the column honest about what the user will get.
            Assert.That(Languages.ToSupported(input), Is.EqualTo(Languages.English));
        }
    }
}
