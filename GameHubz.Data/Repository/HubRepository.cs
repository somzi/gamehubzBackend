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
                    NumberOfTournaments = x.Tournaments!= null ? x.Tournaments.Count() : 0,
                    UserDisplayName = x.User.FirstName + " " + x.User.LastName
                })
                .ToListAsync();
        }

        public async Task<HubEntity> GetWithDetailsById(Guid id)
        {
            return await this.BaseDbSet()
               .Include(x => x.UserHubs)
               .Include(x => x.Tournaments)
               .Where(x => x.Id == id)
               .SingleAsync();
        }
    }
}