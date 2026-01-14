namespace GameHubz.Logic.Interfaces
{
    public interface IUserRepository : IRepository<UserEntity>
    {
        Task<UserEntity?> GetByIdForEdit(Guid userId);

        Task<UserEntity?> GetByForgotPasswordToken(Guid getByForgotPasswordToken);

        Task<UserEntity?> GetByVerifyEmailToken(Guid verifyEmailToken);

        Task<UserEmailAndId?> GetIdByEmail(string email);

        Task<bool> AnyByEmail(string email);

        Task<UserEntity?> ShallowGetByEmail(string email);

        Task<UserEntity?> GetByEmail(string email);

        Task<UserEntity?> GetUserByObjectId(string objectId);

        Task<UserRoleEntity?> GetUserRole(Guid userId);

        Task<IEnumerable<UserEntity>> GetUsers(IList<FilterItem> filterItems, IList<SortItem> sortItems, int pageIndex, int pageSize);

        Task<List<LookupResponse>> GetUsersByIds(List<Guid> ids);

        Task<UserEntity?> GetByIdWithRefreshTokens(Guid userId);

        Task<List<LookupResponse>> GetUserLookups();

        bool IsEmailUnique(UserEntity entity, string email);

        bool IsObjectIdUnique(UserEntity entity, string? objectId);

        Task<UserEntity> GetWithSocials(Guid id);

        Task<UserEntity> GetWithSocialsAndStats(Guid id);
    }
}