using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Security;

[TestFixture]
public sealed class JoinCodeThrottleTests
{
    [Test]
    public async Task OnlyWrongCodesCount_TheSixthAttemptAfterFiveWrongWaits()
    {
        var cache = new FakeCacheService();
        var localization = new LocalizationServiceFactory().CreateService();
        var playerId = Guid.NewGuid();
        var tournament = new TournamentEntity { IsPrivate = true, JoinCode = "123456" };
        var key = $"join_code_attempts:{playerId}";

        // An empty or malformed entry is not a guess and does not use the budget.
        Assert.ThrowsAsync<BusinessRuleException>(() => TournamentJoinCodes.EnsureCanEnterAsync(
            tournament, null, playerId, cache, localization));
        Assert.That(await cache.GetCounterAsync(key), Is.Zero);

        // A correct code gives its reservation back, however often it is used (lookup, then
        // registration, then a team action all reuse the same code).
        for (var i = 0; i < 10; i++)
            await TournamentJoinCodes.EnsureCanEnterAsync(tournament, "123456", playerId, cache, localization);
        Assert.That(await cache.GetCounterAsync(key), Is.Zero);

        // Four wrong codes, and the right one still gets through without costing anything.
        for (var i = 0; i < 4; i++)
            await ExpectWrong(tournament, playerId, cache, localization);
        await TournamentJoinCodes.EnsureCanEnterAsync(tournament, "123456", playerId, cache, localization);
        Assert.That(await cache.GetCounterAsync(key), Is.EqualTo(4));

        // The fifth wrong code is still a plain "wrong code"; after it, even the right one waits.
        await ExpectWrong(tournament, playerId, cache, localization);
        var limit = Assert.ThrowsAsync<BusinessRuleException>(() => TournamentJoinCodes.EnsureCanEnterAsync(
            tournament, "123456", playerId, cache, localization));
        Assert.That(limit!.ErrorCode, Is.EqualTo("join_code_limit"));

        // One player's limit does not lock out another player.
        await TournamentJoinCodes.EnsureCanEnterAsync(
            tournament, "123456", Guid.NewGuid(), cache, localization);
    }

    private static Task ExpectWrong(TournamentEntity tournament, Guid playerId, ICacheService cache, GameHubz.Logic.Interfaces.ILocalizationService localization)
    {
        var wrong = Assert.ThrowsAsync<BusinessRuleException>(() => TournamentJoinCodes.EnsureCanEnterAsync(
            tournament, "654321", playerId, cache, localization));
        Assert.That(wrong!.ErrorCode, Is.EqualTo("join_code_wrong"));
        return Task.CompletedTask;
    }

    [Test]
    public void CounterUnavailable_RefusesCodeChecks()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));
        var localization = new LocalizationServiceFactory().CreateService();

        var unavailable = Assert.ThrowsAsync<BusinessRuleException>(() => TournamentJoinCodes.ConsumeAttemptAsync(
            cache.Object, localization, Guid.NewGuid()));
        Assert.That(unavailable!.ErrorCode, Is.EqualTo("join_code_unavailable"));
    }

    [TestCase(null, "join_code_required")]
    [TestCase("654321", "join_code_wrong")]
    public void RejectedCode_HasAStableRecoveryReason(string? code, string errorCode)
    {
        var exception = Assert.ThrowsAsync<BusinessRuleException>(() => TournamentJoinCodes.EnsureCanEnterAsync(
            new TournamentEntity { IsPrivate = true, JoinCode = "123456" }, code, Guid.NewGuid(),
            new FakeCacheService(), new LocalizationServiceFactory().CreateService()));
        Assert.That(exception!.ErrorCode, Is.EqualTo(errorCode));
    }

    [Test]
    public async Task ParallelChecks_OnlyFivePassTheSharedAtomicCounter()
    {
        var used = 0;
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => (long)Interlocked.Increment(ref used));
        var playerId = Guid.NewGuid();
        var localization = new LocalizationServiceFactory().CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
        {
            try
            {
                await TournamentJoinCodes.ConsumeAttemptAsync(cache.Object, localization, playerId);
                return true;
            }
            catch (BusinessRuleException exception) when (exception.ErrorCode == "join_code_limit")
            {
                return false;
            }
        })));
        Assert.That(results.Count(passed => passed), Is.EqualTo(5));
    }
}
