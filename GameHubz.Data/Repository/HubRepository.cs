using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class HubRepository : BaseRepository<ApplicationContext, HubEntity>, IHubRepository
    {
        public HubRepository(ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<HubEntity>> GetByUserId(Guid userId)
        {
            return await this.BaseDbSet()
            .Where(x => x.UserId == userId)
            .ToListAsync();
        }

        public async Task<List<HubDto>> GetOverview()
        {
            return await this.BaseDbSet()
                .Select(x => new HubDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    UserId = x.UserId,
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count() : 0,
                    NumberOfTournaments = x.Tournaments != null ? x.Tournaments.Count() : 0,
                    UserDisplayName = x.User.FirstName + " " + x.User.LastName
                })
                .ToListAsync();
        }

        public async Task<HubOverviewDto?> GetOverviewDtoById(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == hubId)
                .Select(x => new HubOverviewDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count : 0,
                    NumberOfTournaments = x.Tournaments != null ? x.Tournaments.Count : 0,
                    UserId = x.UserId,
                    AvatarUrl = x.AvatarUrl,
                    OwnerName = x.User.Username,
                    HubSocials = x.HubSocials != null
                            ? x.HubSocials.Select(s => new HubSocialDto
                            {
                                Id = s.Id,
                                HubId = s.HubId,
                                Type = s.Type,
                                Username = s.Username
                            }).ToList()
                            : new List<HubSocialDto>()
                })
                .FirstOrDefaultAsync();
        }

        public Task<bool> IsUserFollowingHub(Guid userId, Guid id)
        {
            return this.BaseDbSet()
                .Where(x => x.Id == id)
                .AnyAsync(x => x.UserHubs != null && x.UserHubs.Any(uh => uh.UserId == userId));
        }

        public async Task<IEnumerable<HubDto>> GetHubsByUserId(Guid userId, int pageNumber, bool joined, string? search = null)
        {
            return await this.BaseDbSet()
                .Where(x => joined
                    ? x.UserHubs!.Any(uh => uh.UserId == userId) || x.UserId == userId
                    : !x.UserHubs!.Any(uh => uh.UserId == userId) && x.UserId != userId)
                .Where(x => search == null || x.Name.StartsWith(search))
                .Skip(pageNumber * 10)
                .Take(10)
                .Select(x => new HubDto
                {
                    Id = x.Id!.Value,
                    Name = x.Name,
                    Description = x.Description,
                    UserId = x.UserId,
                    NumberOfUsers = x.UserHubs != null ? x.UserHubs.Count() : 0,
                    NumberOfTournaments = x.Tournaments != null ? x.Tournaments.Count() : 0,
                    UserDisplayName = x.User.FirstName + " " + x.User.LastName,
                    AvatarUrl = x.AvatarUrl
                })
                .ToListAsync();
        }

        public async Task<List<Guid>> GetHubIdsByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(h => h.UserId == userId || (h.UserHubs != null && h.UserHubs.Any(uh => uh.UserId == userId)))
                .Select(h => h.Id!.Value)
                .ToListAsync();
        }
    }
}