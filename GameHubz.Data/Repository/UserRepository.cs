using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.Data.Extensions;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
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
                .Where(x => x.Email == email && x.IsActive)
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
            if (string.IsNullOrWhiteSpace(email))
            {
                return true;
            }

            return entity.IsNew
                ? !this.BaseDbSet().Any(x => x.Email == email && x.IsActive)
                : !this.BaseDbSet().Any(x => x.Id != entity.Id && x.Email == email && x.IsActive);
        }

        public bool IsObjectIdUnique(UserEntity entity, string? objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return true;
            }

            return entity.IsNew
                ? !this.BaseDbSet().Any(x => x.ObjectId == objectId)
                : !this.BaseDbSet().Any(x => x.Id != entity.Id && x.ObjectId == objectId);
        }

        protected override IQueryable<UserEntity> DbSetForSingleAndList()
        {
            return base.DbSetForSingleAndList()
                .Include(x => x.UserRole);
        }

        public async Task<UserEntity> GetWithSocials(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.UserSocials)
                .Where(x => x.Id == id)
                .SingleAsync();
        }

        public async Task<UserEntity> GetWithSocialsAndStats(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.UserSocials)
                .Where(x => x.Id == id)
                .SingleAsync();
        }

        public async Task<UserEntity?> GetByOtpAndMail(ResetPasswordOtpRequestDto resetPasswordRequestDto)
        {
            return await this.BaseDbSet()
                .Where(x => x.ForgotPasswordOtp == resetPasswordRequestDto.OtpCode && x.Email == resetPasswordRequestDto.Email)
                .SingleAsync();
        }

        public async Task ClearPushTokenAsync(string pushToken)
        {
            await this.BaseDbSet()
                .Where(x => x.PushToken == pushToken)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PushToken, (string?)null));
        }
    }
}