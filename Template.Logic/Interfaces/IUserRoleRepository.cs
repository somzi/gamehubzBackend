namespace Template.Logic.Interfaces
{
    public interface IUserRoleRepository : IRepository<UserRoleEntity>
    {
        UserRoleEntity? FindBySystemName(string systemName);

        Task<List<LookupResponse>> GetUserRoleLookups();
    }
}