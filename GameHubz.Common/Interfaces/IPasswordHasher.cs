namespace GameHubz.Common.Interfaces
{
    public interface IPasswordHasher
    {
        string HashPassword(string password, string passwordSalt);
    }
}
