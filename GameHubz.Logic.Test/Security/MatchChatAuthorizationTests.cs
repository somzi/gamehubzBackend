using System;
using System.Collections.Generic;
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

        private static readonly Guid MatchId = Guid.Parse("10ed9e3f-5aee-4eeb-8172-1f66ddd8014d");

        private static (MatchChatService Service, Guid HomeUserId, Guid TournamentId) BuildService(
            Guid? callerId,
            MatchStatus status)
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
                IsDeleted = false,
            });
            context.SaveChanges();

            var token = new TokenUserInfo
            {
                UserId = effectiveCallerId,
                Role = "User",
                RoleEnum = GameHubz.Common.Consts.UserRoleEnum.BasicUser,
            };
            var userContext = new Mock<IUserContextReader>();
            userContext.Setup(reader => reader.GetTokenUserInfoFromContext())
                .Returns(Task.FromResult<TokenUserInfo?>(token));
            userContext.Setup(reader => reader.GetTokenUserInfoFromContextThrowIfNull())
                .Returns(Task.FromResult(token));

            var factory = new TestUnitOfWorkFactory(context, localization);
            var cache = new FakeCacheService();
            cache.SetAsync<bool?>($"tournament_authz:{effectiveCallerId}:{tournamentId}", false)
                .GetAwaiter().GetResult();
            var tournamentAuth = new TournamentAuthorizationService(
                factory, userContext.Object, localization, cache, userHubService: null!);

            var service = new MatchChatService(
                factory,
                mapper: null!,
                localization,
                new Mock<IValidator<MatchChatEntity>>().Object,
                searchService: null!,
                serviceFunctions: null!,
                userContext.Object,
                new Mock<IHubContext<MatchChatHub>>().Object,
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
