using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GameHubz.Api.Controllers;
using GameHubz.Common.Consts;
using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Security;

[TestFixture]
public sealed class PrivateTeamJoinTests
{
    [TestCase(false, null, false)]
    [TestCase(false, "654321", false)]
    [TestCase(false, "123456", true)]
    [TestCase(true, "123456", false)]
    public async Task DirectJoin_RequiresTheCode_AndHonorsCaptainApproval(
        bool requiresApproval, string? code, bool shouldJoin)
    {
        using var context = NewContext();
        var (teamId, tournamentId, applicantId) = await SeedTeam(context, requiresApproval, isPrivate: true);
        var service = BuildService(context, applicantId, tournamentId);

        if (shouldJoin)
            await service.JoinTeam(teamId, code);
        else
            Assert.ThrowsAsync<BusinessRuleException>(() => service.JoinTeam(teamId, code));

        context.ChangeTracker.Clear();
        Assert.That(await context.Set<TournamentTeamMemberEntity>().CountAsync(m => m.UserId == applicantId),
            Is.EqualTo(shouldJoin ? 1 : 0));
    }

    [Test]
    public async Task RequestJoin_PrivateTeam_RequiresCodeBeforeCreatingRequest()
    {
        using var context = NewContext();
        var (teamId, tournamentId, applicantId) = await SeedTeam(context, requiresApproval: true, isPrivate: true);
        var service = BuildService(context, applicantId, tournamentId);

        Assert.ThrowsAsync<BusinessRuleException>(() => service.RequestJoin(teamId));
        context.ChangeTracker.Clear();
        Assert.That(await context.Set<TeamJoinRequestEntity>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ExistingPublicTeam_StillAcceptsDirectJoinWithoutCode()
    {
        using var context = NewContext();
        var (teamId, tournamentId, applicantId) = await SeedTeam(context, requiresApproval: false, isPrivate: false);
        var service = BuildService(context, applicantId, tournamentId);
        await service.JoinTeam(teamId);
        context.ChangeTracker.Clear();
        Assert.That(await context.Set<TournamentTeamMemberEntity>().CountAsync(m => m.UserId == applicantId), Is.EqualTo(1));
    }

    private static ApplicationContext NewContext() => new(new DbContextOptionsBuilder<ApplicationContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Test]
    public async Task LegacyPublicJoin_WithoutRequestBody_StillWorksOverHttp()
    {
        using var context = NewContext();
        var (teamId, tournamentId, applicantId) = await SeedTeam(context, false, false);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(TournamentTeamController).Assembly);
        builder.Services.AddSingleton(BuildService(context, applicantId, tournamentId));
        await using var app = builder.Build();
        // Identity comes from the mocked user-context reader; this test exercises MVC body binding.
        app.MapControllers().AllowAnonymous();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await client.PostAsync($"/api/teams/{teamId}/join", null);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            context.ChangeTracker.Clear();
            Assert.That(await context.Set<TournamentTeamMemberEntity>().CountAsync(m => m.UserId == applicantId), Is.EqualTo(1));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<(Guid TeamId, Guid TournamentId, Guid ApplicantId)> SeedTeam(
        ApplicationContext context, bool requiresApproval, bool isPrivate)
    {
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();
        context.AddRange(
            new UserEntity { Id = captainId, Username = "captain" },
            new UserEntity { Id = applicantId, Username = "applicant" },
            new TournamentEntity { Id = tournamentId, Name = "Tournament", IsTeamTournament = true,
                IsPrivate = isPrivate, JoinCode = isPrivate ? "123456" : null,
                TeamSize = 2, Status = TournamentStatus.RegistrationOpen },
            new TournamentTeamEntity { Id = teamId, TeamName = "Team", TournamentId = tournamentId,
                CaptainUserId = captainId, RequiresApproval = requiresApproval },
            new TournamentTeamMemberEntity { Id = Guid.NewGuid(), TeamId = teamId, UserId = captainId });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (teamId, tournamentId, applicantId);
    }

    private static TournamentTeamService BuildService(ApplicationContext context, Guid applicantId, Guid tournamentId)
    {
        var token = new TokenUserInfo { UserId = applicantId, Username = "applicant",
            Role = "BasicUser", RoleEnum = UserRoleEnum.BasicUser };
        var reader = new Mock<IUserContextReader>();
        reader.Setup(r => r.GetTokenUserInfoFromContext()).ReturnsAsync(token);
        reader.Setup(r => r.GetTokenUserInfoFromContextThrowIfNull()).ReturnsAsync(token);
        var localization = new LocalizationServiceFactory().CreateService();
        var factory = new TestUnitOfWorkFactory(context, localization);
        var cache = new FakeCacheService();
        cache.SetAsync<bool?>($"tournament_authz:{applicantId}:{tournamentId}", false).GetAwaiter().GetResult();
        var auth = new TournamentAuthorizationService(factory, reader.Object, localization, cache, null!);
        return new TournamentTeamService(factory, reader.Object, localization, cache, null!, null!, auth);
    }
}
