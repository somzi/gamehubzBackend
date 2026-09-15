using System.Collections.Generic;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using NUnit.Framework;

using GameHubz.DataModels.Consts;
using GameHubz.Localization;
using GameHubz.Logic.Extensions;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Test.Services
{
    // Plural() is the one branching rule the Slavic languages added, and its mistakes are invisible
    // until someone's count happens to end in the wrong digit: "21 матчей" instead of "21 матч" only
    // shows up for a Russian user with exactly 21 of something. These pin the CLDR forms for every
    // shipped language through the real resx strings, including the sr/ru/uk "one" forms that carry
    // {0} because they also cover 21, 31, 101…
    [TestFixture]
    internal sealed class LocalizationPluralTests
    {
        private const string OneKey = "BusinessRule.MatchCountOne";
        private const string FewKey = "BusinessRule.MatchCountFew";
        private const string ManyKey = "BusinessRule.MatchCountMany";

        private static ILocalizationService ServiceWithHeader(string? header)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Language"] = Languages.English })
                .Build();

            var context = new DefaultHttpContext();
            if (header is not null)
            {
                context.Request.Headers["Language"] = header;
            }

            return new LocalizationService(config, new HttpContextAccessor { HttpContext = context });
        }

        // Polish: "one" is exactly 1 — 21 is "many".
        [TestCase(Languages.Polish, 0, "0 meczów")]
        [TestCase(Languages.Polish, 1, "1 mecz")]
        [TestCase(Languages.Polish, 2, "2 mecze")]
        [TestCase(Languages.Polish, 4, "4 mecze")]
        [TestCase(Languages.Polish, 5, "5 meczów")]
        [TestCase(Languages.Polish, 12, "12 meczów")]
        [TestCase(Languages.Polish, 14, "14 meczów")]
        [TestCase(Languages.Polish, 21, "21 meczów")]
        [TestCase(Languages.Polish, 22, "22 mecze")]
        [TestCase(Languages.Polish, 25, "25 meczów")]
        [TestCase(Languages.Polish, 101, "101 meczów")]
        [TestCase(Languages.Polish, 112, "112 meczów")]
        // Serbian: "one" ends in 1 but not 11; no separate "many".
        [TestCase(Languages.Serbian, 0, "0 mečeva")]
        [TestCase(Languages.Serbian, 1, "1 meč")]
        [TestCase(Languages.Serbian, 2, "2 meča")]
        [TestCase(Languages.Serbian, 5, "5 mečeva")]
        [TestCase(Languages.Serbian, 11, "11 mečeva")]
        [TestCase(Languages.Serbian, 12, "12 mečeva")]
        [TestCase(Languages.Serbian, 21, "21 meč")]
        [TestCase(Languages.Serbian, 22, "22 meča")]
        [TestCase(Languages.Serbian, 25, "25 mečeva")]
        [TestCase(Languages.Serbian, 101, "101 meč")]
        [TestCase(Languages.Serbian, 111, "111 mečeva")]
        // Russian.
        [TestCase(Languages.Russian, 0, "0 матчей")]
        [TestCase(Languages.Russian, 1, "1 матч")]
        [TestCase(Languages.Russian, 3, "3 матча")]
        [TestCase(Languages.Russian, 5, "5 матчей")]
        [TestCase(Languages.Russian, 11, "11 матчей")]
        [TestCase(Languages.Russian, 13, "13 матчей")]
        [TestCase(Languages.Russian, 21, "21 матч")]
        [TestCase(Languages.Russian, 24, "24 матча")]
        [TestCase(Languages.Russian, 101, "101 матч")]
        [TestCase(Languages.Russian, 111, "111 матчей")]
        // Ukrainian.
        [TestCase(Languages.Ukrainian, 1, "1 матч")]
        [TestCase(Languages.Ukrainian, 2, "2 матчі")]
        [TestCase(Languages.Ukrainian, 5, "5 матчів")]
        [TestCase(Languages.Ukrainian, 11, "11 матчів")]
        [TestCase(Languages.Ukrainian, 21, "21 матч")]
        [TestCase(Languages.Ukrainian, 22, "22 матчі")]
        [TestCase(Languages.Ukrainian, 112, "112 матчів")]
        // English, Spanish, Portuguese: binary, and 21 is plural.
        [TestCase(Languages.English, 0, "0 matches")]
        [TestCase(Languages.English, 1, "1 match")]
        [TestCase(Languages.English, 2, "2 matches")]
        [TestCase(Languages.English, 21, "21 matches")]
        [TestCase(Languages.Spanish, 1, "1 partido")]
        [TestCase(Languages.Spanish, 2, "2 partidos")]
        [TestCase(Languages.Portuguese, 1, "1 partida")]
        [TestCase(Languages.Portuguese, 21, "21 partidas")]
        public void Plural_PicksTheFormForTheRequestLanguage(string language, int count, string expected)
        {
            ILocalizationService service = ServiceWithHeader(language);

            Assert.That(service.Plural(count, OneKey, FewKey, ManyKey), Is.EqualTo(expected));
        }

        // The push / e-mail overload words the label for the recipient, whatever the caller's header says.
        [TestCase(Languages.Russian, 22, "22 матча")]
        [TestCase(Languages.Polish, 21, "21 meczów")]
        [TestCase(Languages.Serbian, 21, "21 meč")]
        [TestCase(Languages.Ukrainian, 5, "5 матчів")]
        public void Plural_WithExplicitLanguage_IgnoresTheRequestHeader(string language, int count, string expected)
        {
            ILocalizationService service = ServiceWithHeader(Languages.English);

            Assert.That(service.Plural(count, OneKey, FewKey, ManyKey, language), Is.EqualTo(expected));
        }

        [Test]
        public void Plural_WithNullLanguage_UsesTheServerDefault_NotTheCallersHeader()
        {
            // PushText.Resolve words a push with no recipient language in the server default; the
            // count inside it must agree, not switch to the language of whoever triggered the send.
            ILocalizationService service = ServiceWithHeader(Languages.Polish);

            Assert.That(service.Plural(2, OneKey, FewKey, ManyKey, null), Is.EqualTo("2 matches"));
        }
    }
}
