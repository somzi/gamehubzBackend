using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Common.Consts;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Api.Controllers;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Security
{
    [TestFixture]
    internal sealed class GenericControllerSurfaceTests
    {
        [TestCase(typeof(TournamentController), nameof(TournamentController.Delete))]
        [TestCase(typeof(UserHubController), nameof(UserHubController.Delete))]
        [TestCase(typeof(TournamentRegistrationController), nameof(TournamentRegistrationController.Delete))]
        [TestCase(typeof(HubActivityController), nameof(HubActivityController.Delete))]
        [TestCase(typeof(HubActivityController), nameof(HubActivityController.GetById))]
        [TestCase(typeof(HubActivityController), nameof(HubActivityController.GetList))]
        [TestCase(typeof(HubActivityController), nameof(HubActivityController.SaveEntity))]
        [TestCase(typeof(MatchController), nameof(MatchController.Delete))]
        [TestCase(typeof(MatchController), nameof(MatchController.GetById))]
        [TestCase(typeof(MatchController), nameof(MatchController.GetList))]
        [TestCase(typeof(MatchController), nameof(MatchController.SaveEntity))]
        [TestCase(typeof(MatchChatController), nameof(MatchChatController.Delete))]
        [TestCase(typeof(MatchChatController), nameof(MatchChatController.GetById))]
        [TestCase(typeof(MatchChatController), nameof(MatchChatController.GetList))]
        [TestCase(typeof(MatchChatController), nameof(MatchChatController.SaveEntity))]
        public void DisabledGenericAction_IsDeclaredNonAction(Type controllerType, string methodName)
        {
            var method = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SingleOrDefault(candidate => candidate.Name == methodName);

            Assert.That(method, Is.Not.Null, $"{controllerType.Name}.{methodName} must override the inherited action.");
            Assert.That(method!.GetCustomAttribute<NonActionAttribute>(), Is.Not.Null,
                $"{controllerType.Name}.{methodName} must not be exposed as an MVC action.");
        }

        [TestCase(typeof(MatchController), nameof(MatchController.GetDetails))]
        [TestCase(typeof(MatchChatController), nameof(MatchChatController.GetHistory))]
        public void DisabledGenericRoutes_AreAbsentFromMvcActionDiscovery(
            Type controllerType,
            string expectedNamedAction)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddControllers().AddApplicationPart(typeof(MatchController).Assembly);

            using var provider = services.BuildServiceProvider();
            var actions = provider
                .GetRequiredService<IActionDescriptorCollectionProvider>()
                .ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Where(action => action.ControllerTypeInfo.AsType() == controllerType)
                .Select(action => action.ActionName)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(actions, Does.Contain(expectedNamedAction),
                    "the controller must still expose its intended domain endpoint");
                Assert.That(actions, Does.Not.Contain("Delete"));
                Assert.That(actions, Does.Not.Contain("GetById"));
                Assert.That(actions, Does.Not.Contain("GetList"));
                Assert.That(actions, Does.Not.Contain("SaveEntity"));
            });
        }

        [Test]
        public async Task MatchAndMatchChat_GenericCrudRoutes_Return404Or405OverHttp()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(MatchController).Assembly)
                .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(
                    new OnlyControllersFeatureProvider(typeof(MatchController), typeof(MatchChatController))));

            await using var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();

            try
            {
                var addresses = app.Services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!;
                string baseAddress = addresses.Addresses.Single();
                using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
                string id = Guid.NewGuid().ToString();

                var requests = new[]
                {
                    new HttpRequestMessage(HttpMethod.Get, "/api/match"),
                    new HttpRequestMessage(HttpMethod.Get, $"/api/match/{id}"),
                    JsonPost("/api/match"),
                    new HttpRequestMessage(HttpMethod.Delete, $"/api/match/{id}"),
                    new HttpRequestMessage(HttpMethod.Get, "/api/matchchat"),
                    new HttpRequestMessage(HttpMethod.Get, $"/api/matchchat/{id}"),
                    JsonPost("/api/matchchat"),
                    new HttpRequestMessage(HttpMethod.Delete, $"/api/matchchat/{id}"),
                };

                foreach (var request in requests)
                {
                    using (request)
                    using (var response = await client.SendAsync(request))
                    {
                        Assert.That(response.StatusCode,
                            Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed),
                            $"{request.Method} {request.RequestUri} must not reach inherited generic CRUD");
                    }
                }
            }
            finally
            {
                await app.StopAsync();
            }
        }

        private static HttpRequestMessage JsonPost(string path) => new(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public const string SchemeName = "RouteSurfaceTest";

            public TestAuthHandler(
                IOptionsMonitor<AuthenticationSchemeOptions> options,
                ILoggerFactory logger,
                UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var identity = new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
                    SchemeName);
                var principal = new ClaimsPrincipal(identity);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(principal, SchemeName)));
            }
        }

        private sealed class OnlyControllersFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            private readonly HashSet<Type> allowed;

            public OnlyControllersFeatureProvider(params Type[] allowed)
            {
                this.allowed = allowed.ToHashSet();
            }

            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                for (int i = feature.Controllers.Count - 1; i >= 0; i--)
                {
                    if (!allowed.Contains(feature.Controllers[i].AsType()))
                        feature.Controllers.RemoveAt(i);
                }
            }
        }

        [Test]
        public void TournamentRegistration_SaveEntity_RemainsAvailableForMobileRegistration()
        {
            var declaredOverride = typeof(TournamentRegistrationController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SingleOrDefault(method => method.Name == nameof(TournamentRegistrationController.SaveEntity));

            Assert.That(declaredOverride, Is.Null,
                "The inherited POST /api/tournamentRegistration route must remain available.");
        }

        [Test]
        public void PendingRegistrations_RejectsNonManager()
        {
            var tournamentId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var (service, cache) = CreateRegistrationService(userId, "User");
            cache.SetAsync<bool?>($"tournament_authz:{userId}:{tournamentId}", false).GetAwaiter().GetResult();

            Assert.ThrowsAsync<UnauthorizedAccessToServiceException>(
                () => service.GetPendingByTournamentId(tournamentId));
        }

        [Test]
        public async Task PendingRegistrations_AllowsAdmin()
        {
            var (service, _) = CreateRegistrationService(Guid.NewGuid(), "Admin");

            var result = await service.GetPendingByTournamentId(Guid.NewGuid());

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void BulkApproval_RejectsRegistrationsFromDifferentTournaments()
        {
            var firstTournamentId = Guid.NewGuid();
            var secondTournamentId = Guid.NewGuid();
            var firstRegistrationId = Guid.NewGuid();
            var secondRegistrationId = Guid.NewGuid();

            var (service, _) = CreateRegistrationService(
                Guid.NewGuid(),
                "Admin",
                context =>
                {
                    context.Set<TournamentEntity>().AddRange(
                        new TournamentEntity
                        {
                            Id = firstTournamentId,
                            Name = "First",
                            Status = TournamentStatus.RegistrationOpen,
                            IsDeleted = false,
                        },
                        new TournamentEntity
                        {
                            Id = secondTournamentId,
                            Name = "Second",
                            Status = TournamentStatus.RegistrationOpen,
                            IsDeleted = false,
                        });

                    context.Set<TournamentRegistrationEntity>().AddRange(
                        new TournamentRegistrationEntity
                        {
                            Id = firstRegistrationId,
                            TournamentId = firstTournamentId,
                            UserId = Guid.NewGuid(),
                            Status = TournamentRegistrationStatus.Pending,
                            IsDeleted = false,
                        },
                        new TournamentRegistrationEntity
                        {
                            Id = secondRegistrationId,
                            TournamentId = secondTournamentId,
                            UserId = Guid.NewGuid(),
                            Status = TournamentRegistrationStatus.Pending,
                            IsDeleted = false,
                        });

                    context.SaveChanges();
                });

            Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveRegistrations(
                new List<Guid> { firstRegistrationId, secondRegistrationId }));
        }

        private static (TournamentRegistrationService Service, FakeCacheService Cache) CreateRegistrationService(
            Guid userId,
            string role,
            Action<ApplicationContext>? seed = null)
        {
            var localization = new LocalizationServiceFactory().CreateService();
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationContext(options);
            seed?.Invoke(context);
            var factory = new TestUnitOfWorkFactory(context, localization);
            var cache = new FakeCacheService();
            var token = new TokenUserInfo
            {
                UserId = userId,
                Role = role,
                RoleEnum = role == "Admin" ? UserRoleEnum.Admin : UserRoleEnum.BasicUser
            };
            var userContext = new Mock<IUserContextReader>();
            userContext.Setup(reader => reader.GetTokenUserInfoFromContext())
                .Returns(Task.FromResult<TokenUserInfo?>(token));

            var tournamentAuth = new TournamentAuthorizationService(
                factory,
                userContext.Object,
                localization,
                cache,
                userHubService: null!);

            var service = new TournamentRegistrationService(
                factory,
                mapper: null!,
                localization,
                validator: null!,
                searchService: null!,
                serviceFunctions: null!,
                userContext.Object,
                tournamentParticipantService: null!,
                cache,
                badgeService: null!,
                tournamentAuth);

            return (service, cache);
        }
    }
}
