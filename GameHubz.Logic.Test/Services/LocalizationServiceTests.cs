using System.Collections.Generic;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using NUnit.Framework;

using GameHubz.DataModels.Consts;
using GameHubz.Localization;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Test.Services
{
    // LocalizationService is a singleton shared by ~100 services, so the language it answers in
    // must come from the CURRENT request rather than from anything captured at construction.
    // These tests pin that down, plus the explicit-language overload the push/e-mail paths need.
    [TestFixture]
    internal sealed class LocalizationServiceTests
    {
        private const string SomeKey = "Exception.EmptyEmail";
        private const string SomeKeyEn = "Email is required";
        private const string SomeKeyEs = "El correo electrónico es obligatorio";
        private const string SomeKeyPt = "O e-mail é obrigatório";
        private const string SomeKeySr = "Imejl je obavezan";

        private static IConfiguration Config(string? language) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(language is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["Language"] = language })
                .Build();

        private static IHttpContextAccessor AccessorWithHeader(string? header)
        {
            var context = new DefaultHttpContext();

            if (header is not null)
            {
                context.Request.Headers["Language"] = header;
            }

            return new HttpContextAccessor { HttpContext = context };
        }

        [Test]
        public void NoHeader_And_NoConfig_FallsBackToEnglish()
        {
            // Production appsettings.json defines no "Language" key at all.
            ILocalizationService service = new LocalizationService(Config(null));

            Assert.That(service[SomeKey], Is.EqualTo(SomeKeyEn));
        }

        [Test]
        public void SpanishHeader_ResolvesSpanish()
        {
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.Spanish));

            Assert.That(service[SomeKey], Is.EqualTo(SomeKeyEs));
        }

        [Test]
        public void PortugueseHeader_ResolvesPortuguese()
        {
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.Portuguese));

            Assert.That(service[SomeKey], Is.EqualTo(SomeKeyPt));
        }

        [Test]
        public void RegionalAndCasedTags_AreNormalised()
        {
            foreach (string header in new[] { "es-419", "ES", " es " })
            {
                ILocalizationService service = new LocalizationService(
                    Config(Languages.English), AccessorWithHeader(header));

                Assert.That(service[SomeKey], Is.EqualTo(SomeKeyEs), $"header '{header}'");
            }

            // The resource set is written in pt-BR, so a Portugal tag reads it too rather
            // than falling through to English.
            foreach (string header in new[] { "pt-BR", "pt-PT", "PT" })
            {
                ILocalizationService service = new LocalizationService(
                    Config(Languages.English), AccessorWithHeader(header));

                Assert.That(service[SomeKey], Is.EqualTo(SomeKeyPt), $"header '{header}'");
            }
        }

        [Test]
        public void SerbianHeader_ResolvesSerbian()
        {
            // 'sr' used to be a request-data default with no resource set, so it rendered English.
            // No client ever sent it (the app only sends codes it ships), so it now names Serbian.
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.Serbian));

            Assert.That(service[SomeKey], Is.EqualTo(SomeKeySr));
        }

        [Test]
        public void ExplicitLanguageOverload_IgnoresTheRequestHeader()
        {
            // A push notification is written for the recipient, not for whoever triggered it.
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.English));

            Assert.That(service[SomeKey, Languages.Spanish], Is.EqualTo(SomeKeyEs));
            Assert.That(service[SomeKey, Languages.Portuguese], Is.EqualTo(SomeKeyPt));
            Assert.That(service[SomeKey, Languages.English], Is.EqualTo(SomeKeyEn));
        }

        [Test]
        public void ExplicitLanguage_UnknownOrEmpty_FallsBackToConfigured()
        {
            ILocalizationService service = new LocalizationService(Config(Languages.English));

            Assert.That(service[SomeKey, "de"], Is.EqualTo(SomeKeyEn));
            Assert.That(service[SomeKey, ""], Is.EqualTo(SomeKeyEn));
        }

        [Test]
        public void SameInstance_AnswersPerRequest_NotPerConstruction()
        {
            // The singleton must not latch onto the first language it ever saw.
            var accessor = new HttpContextAccessor();
            ILocalizationService service = new LocalizationService(Config(Languages.English), accessor);

            var spanish = new DefaultHttpContext();
            spanish.Request.Headers["Language"] = Languages.Spanish;
            accessor.HttpContext = spanish;
            Assert.That(service[SomeKey], Is.EqualTo(SomeKeyEs));

            var english = new DefaultHttpContext();
            english.Request.Headers["Language"] = Languages.English;
            accessor.HttpContext = english;
            Assert.That(service[SomeKey], Is.EqualTo(SomeKeyEn));
        }

        [Test]
        public void FormattedHelpers_FollowTheRequestLanguage()
        {
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.Spanish));

            Assert.That(service.PropertyIsEmptyMessage("Email"), Is.EqualTo("Email es obligatorio"));
        }

        [Test]
        public void KeyMissingFromSpanish_FallsBackToEnglishNotToTheKeyName()
        {
            ILocalizationService service = new LocalizationService(
                Config(Languages.English), AccessorWithHeader(Languages.Spanish));

            const string unknown = "Definitely.Not.A.Real.Key";
            Assert.That(service[unknown], Is.EqualTo("(no translation)"));
        }
    }
}
