using Microsoft.EntityFrameworkCore;
using Template.Data.Base;
using Template.Data.Context;
using Template.DataModels.Domain;
using Template.DataModels.Models;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data.Repository
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