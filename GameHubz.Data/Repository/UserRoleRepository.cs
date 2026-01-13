using Microsoft.EntityFrameworkCore;
using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data.Repository
{
    public class UserRoleRepository : BaseRepository<ApplicationContext, UserRoleEntity>, IUserRoleRepository
    {
        public UserRoleRepository(ApplicationContext context, DateTimeProvider dateTimeProvider, IFilterExpressionBuilder filterExpressionBuilder, ISortStringBuilder sortStringBuilder, ILocalizationService localizationService) : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public UserRoleEntity? FindBySystemName(string systemName)
        {
            return this.DbSetForSingle().Where(x => x.SystemName == systemName).SingleOrDefault();
        }

        public Task<List<LookupResponse>> GetUserRoleLookups()
        {
            return this.BaseDbSet()
                 .Select(x => new LookupResponse()
                 {
                     Id = x.Id!.Value,
                     DisplayText = x.DisplayName,
                 })
                 .ToListAsync();
        }
    }
}
