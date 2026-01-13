using Microsoft.EntityFrameworkCore;
using Template.Common.Interfaces;
using Template.Common.Models;
using Template.Data.Base;
using Template.Data.Context;
using Template.Data.Extensions;
using Template.DataModels.Domain;
using Template.DataModels.Models;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data.Repository
{
    public class UserRepository : BaseRepository<ApplicationContext, UserEntity>, IUserRepository
    {
        public UserRepository(ApplicationContext context, DateTimeProvider dateTimeProvider, IFilterExpressionBuilder filterExpressionBuilder, ISortStringBuilder sortStringBuilder, ILocalizationService localizationService) : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<UserEntity?> GetByIdWithRefreshTokens(Guid userId)
        {
            return await this.BaseDbSet()
                .Include(x => x.UserRole)
                .Include(x => x.RefreshTokens)
                .Where(x => x.Id == userId)
                .SingleOrDefaultAsync();
        }

        public async Task<UserEmailAndId?> GetIdByEmail(string email)
        {
            return await this.BaseDbSet()
                .Select(x => new UserEmailAndId(x.Id, x.Email))
                .Where(x => x.Email == email)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> AnyByEmail(string email)
        {
            return await this.BaseDbSet()
                .Where(x => x.Email == email)
                .AnyAsync();
        }

        public async Task<UserEntity?> GetByEmail(string email)
        {
            return await this.BaseDbSet()
                .Include(x => x.UserRole)
                .Where(x => x.Email == email)
                .SingleOrDefaultAsync();
        }

        public async Task<UserEntity?> ShallowGetByEmail(string email)
        {
            return await this.BaseDbSet()
                .Where(x => x.Email == email)
                .SingleOrDefaultAsync();
        }

        public async Task<UserEntity?> GetByForgotPasswordToken(Guid forgotPasswordToken)
        {
            return await this.BaseDbSet()
                .Where(x => x.ForgotPasswordToken == forgotPasswordToken)
                .SingleOrDefaultAsync();
        }

        public async Task<UserEntity?> GetByVerifyEmailToken(Guid verifyEmailToken)
        {
            return await this.BaseDbSet()
                .Where(x => x.VerifyEmailToken == verifyEmailToken)
                .SingleOrDefaultAsync();
        }

        public async Task<UserEntity?> GetByIdForEdit(Guid userId)
        {
            return await this.DbSetForSingle()
                .Where(x => x.Id == userId)
                .SingleOrDefaultAsync();
        }

        public Task<UserEntity?> GetUserByObjectId(string objectId)
        {
            return this.BaseDbSet()
                .Include(x => x.UserRole)
                .Where(x => x.ObjectId == objectId)
                .FirstOrDefaultAsync();
        }

        public Task<UserRoleEntity?> GetUserRole(Guid userId)
        {
            return this.BaseDbSet()
                .Include(x => x.UserRole)
                .Where(x => x.Id == userId)
                .Select(x => x.UserRole)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<UserEntity>> GetUsers(IList<FilterItem> filterItems, IList<SortItem> sortItems, int pageIndex, int pageSize)
        {
            IFilterCompiled<UserEntity> filterCompiled = this.FilterExpressionBuilder.CompileFilter<UserEntity>(filterItems);
            var sortString = this.SortStringBuilder.CreateSortString(sortItems);

            IQueryable<UserEntity> users = this.DbSetForList();

            return await users
                 .Where(filterCompiled.Expression)
                 .OrderByConditional(sortString)
                 .Include(x => x.UserRole)
                 .Skip(pageIndex * pageSize)
                 .Take(pageSize)
                 .ToListAsync();
        }

        public async Task<List<LookupResponse>> GetUserLookups()
        {
            return await this.BaseDbSet()
                 .Select(x => new LookupResponse()
                 {
                     Id = x.Id!.Value,
                     DisplayText = $"{x.FirstName} {x.LastName}",
                 })
                 .ToListAsync();
        }

        public async Task<List<LookupResponse>> GetUsersByIds(List<Guid> ids)
        {
            return await this.DbSetForList()
                .Where(x => ids.Contains(x.Id!.Value))
                .Select(x => new LookupResponse(
                    x.Id!.Value,
                    $"{x.FirstName} {x.LastName}")
                ).ToListAsync();
        }

        public bool IsEmailUnique(UserEntity entity, string email)
        {
            return this.IsUnique(entity, email, x => x.Email);
        }

        public bool IsObjectIdUnique(UserEntity entity, string? objectId)
        {
            return this.IsUnique(entity, objectId, x => x.ObjectId);
        }

        protected override IQueryable<UserEntity> DbSetForSingleAndList()
        {
            return base.DbSetForSingleAndList()
                .Include(x => x.UserRole);
        }
    }
}