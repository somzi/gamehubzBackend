using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Common.Consts;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Api.Controllers;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        public void DisabledGenericAction_IsDeclaredNonAction(Type controllerType, string methodName)
        {
            var method = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SingleOrDefault(candidate => candidate.Name == methodName);

            Assert.That(method, Is.Not.Null, $"{controllerType.Name}.{methodName} must override the inherited action.");
            Assert.That(method!.GetCustomAttribute<NonActionAttribute>(), Is.Not.Null,
                $"{controllerType.Name}.{methodName} must not be exposed as an MVC action.");
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

        private static (TournamentRegistrationService Service, FakeCacheService Cache) CreateRegistrationService(
            Guid userId,
            string role)
        {
            var localization = new LocalizationServiceFactory().CreateService();
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var factory = new TestUnitOfWorkFactory(new ApplicationContext(options), localization);
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
