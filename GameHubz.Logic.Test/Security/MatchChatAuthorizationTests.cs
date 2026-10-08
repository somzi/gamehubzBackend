using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentValidation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;

namespace GameHubz.Logic.Test.Security
{
    [TestFixture]
    internal sealed class MatchChatAuthorizationTests
    {
        [Test]
        public void History_RejectsAnOutsider_EvenAfterTheMatchCompleted()
        {
            var outsiderId = Guid.NewGuid();
            var (service, _, _) = BuildService(outsiderId, MatchStatus.Completed);

            Assert.ThrowsAsync<BusinessRuleException>(() => service.GetHistory(MatchId));
        }

        [Test]
        public async Task History_RemainsReadableToAParticipant_AfterTheMatchCompleted()
        {
            var (service, _, _) = BuildService(callerId: null, MatchStatus.Completed);

            var history = await service.GetHistory(MatchId);

            Assert.That(history, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ParticipantWithoutAvailability_IsLockedAndCannotSend(
            bool platformAdmin, bool canManageTournament)
        {
            var (service, _, _) = BuildService(null, MatchStatus.Pending,
                requireAvailability: true, platformAdmin: platformAdmin, canManageTournament: canManageTournament);

            var access = await service.GetAccess(MatchId);

            Assert.That(access.LockedUntilAvailability, Is.True,
                "An admin playing this match must offer availability like every other participant.");
            var error = Assert.ThrowsAsync<BusinessRuleException>(() => service.SendMessage(MatchId, "Hello"));
            Assert.That(error!.Message, Is.EqualTo(
                new LocalizationServiceFactory().CreateService()["BusinessRule.ChatLockedUntilAvailability"]));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task AdminParticipantWithAvailability_CanOpenChatAndSend(
            bool platformAdmin, bool canManageTournament)
        {
            var (service, homeUserId, _) = BuildService(null, MatchStatus.Pending,
                requireAvailability: true, platformAdmin: platformAdmin,
                canManageTournament: canManageTournament, availabilitySet: true);

            Assert.That((await service.GetAccess(MatchId)).LockedUntilAvailability, Is.False);
            var message = await service.SendMessage(MatchId, "Ready to arrange our match");
            Assert.That(message.UserId, Is.EqualTo(homeUserId));
            Assert.That(message.Content, Is.EqualTo("Ready to arrange our match"));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task AdminModeratingAnotherMatch_CanOpenChatAndSendWithoutAvailability(
            bool platformAdmin, bool canManageTournament)
        {
            var adminId = Guid.NewGuid();
            var (service, _, _) = BuildService(adminId, MatchStatus.Pending,
                requireAvailability: true, platformAdmin: platformAdmin, canManageTournament: canManageTournament);

            Assert.That((await service.GetAccess(MatchId)).LockedUntilAvailability, Is.False);
            Assert.That(await service.GetHistory(MatchId), Is.Empty);
            var message = await service.SendMessage(MatchId, "How can I help?");
            Assert.That(message.UserId, Is.EqualTo(adminId));
            Assert.That(message.Content, Is.EqualTo("How can I help?"));
        }

        [Test]
        public async Task ParticipantWithoutAvailability_CanChatWhenRequirementIsDisabled()
        {
            var (service, _, _) = BuildService(null, MatchStatus.Pending);

            Assert.That((await service.GetAccess(MatchId)).LockedUntilAvailability, Is.False);
            await service.SendMessage(MatchId, "Hello");
        }

        [Test]
        public async Task MatchAgreedOutsideApp_KeepsChatOpenWithoutAvailability()
        {
            var (service, _, _) = BuildService(null, MatchStatus.Scheduled, requireAvailability: true);

            Assert.That((await service.GetAccess(MatchId)).LockedUntilAvailability, Is.False);
            await service.SendMessage(MatchId, "We agreed on the time");
        }

        private static readonly Guid MatchId = Guid.Parse("10ed9e3f-5aee-4eeb-8172-1f66ddd8014d");

        private static (MatchChatService Service, Guid HomeUserId, Guid TournamentId) BuildService(
            Guid? callerId,
            MatchStatus status,
            bool requireAvailability = false,
            bool platformAdmin = false,
            bool canManageTournament = false,
            bool availabilitySet = false)
        {
            var tournamentId = Guid.NewGuid();
            var homeParticipantId = Guid.NewGuid();
            var awayParticipantId = Guid.NewGuid();
            var homeUserId = Guid.NewGuid();
            var awayUserId = Guid.NewGuid();
            var effectiveCallerId = callerId ?? homeUserId;

            var localization = new LocalizationServiceFactory().CreateService();
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationContext(options);

            context.Set<TournamentEntity>().Add(new TournamentEntity
            {
                Id = tournamentId,
                Name = "Chat tournament",
                Status = TournamentStatus.InProgress,
                RequireAvailabilityForChat = requireAvailability,
                IsDeleted = false,
            });
            context.Set<TournamentParticipantEntity>().AddRange(
                new TournamentParticipantEntity
                {
                    Id = homeParticipantId,
                    TournamentId = tournamentId,
                    UserId = homeUserId,
                    IsDeleted = false,
                },
                new TournamentParticipantEntity
                {
                    Id = awayParticipantId,
                    TournamentId = tournamentId,
                    UserId = awayUserId,
                    IsDeleted = false,
                });
            context.Set<MatchEntity>().Add(new MatchEntity
            {
                Id = MatchId,
                TournamentId = tournamentId,
                HomeParticipantId = homeParticipantId,
                AwayParticipantId = awayParticipantId,
                Status = status,
                HomeSlots = availabilitySet ? new List<DateTime> { DateTime.UtcNow.AddDays(1) } : new(),
                ScheduledStartTime = status == MatchStatus.Scheduled ? DateTime.UtcNow : null,
                ScheduledOutsideAppByUserId = status == MatchStatus.Scheduled ? effectiveCallerId : null,
                IsDeleted = false,
            });
            context.SaveChanges();

            var token = new TokenUserInfo
            {
                UserId = effectiveCallerId,
                Role = platformAdmin ? "Admin" : "User",
                RoleEnum = platformAdmin
                    ? GameHubz.Common.Consts.UserRoleEnum.Admin
                    : GameHubz.Common.Consts.UserRoleEnum.BasicUser,
            };
            var userContext = new Mock<IUserContextReader>();
            userContext.Setup(reader => reader.GetTokenUserInfoFromContext())
                .Returns(Task.FromResult<TokenUserInfo?>(token));
            userContext.Setup(reader => reader.GetTokenUserInfoFromContextThrowIfNull())
                .Returns(Task.FromResult(token));

            var factory = new TestUnitOfWorkFactory(context, localization);
            var cache = new FakeCacheService();
            cache.SetAsync<bool?>($"tournament_authz:{effectiveCallerId}:{tournamentId}", canManageTournament)
                .GetAwaiter().GetResult();
            var tournamentAuth = new TournamentAuthorizationService(
                factory, userContext.Object, localization, cache, userHubService: null!);

            var client = new Mock<IClientProxy>();
            client.Setup(proxy => proxy.SendCoreAsync(
                    It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var hub = new Mock<IHubContext<MatchChatHub>>();
            hub.Setup(context => context.Clients.Group(MatchId.ToString())).Returns(client.Object);

            var service = new MatchChatService(
                factory,
                mapper: null!,
                localization,
                new Mock<IValidator<MatchChatEntity>>().Object,
                searchService: null!,
                serviceFunctions: null!,
                userContext.Object,
                hub.Object,
                notificationService: null!,
                badgeService: null!,
                tournamentAuth,
                discordDmService: null!,
                cache,
                Options.Create(new ShareLinksConfig()));

            return (service, homeUserId, tournamentId);
        }
    }
}
