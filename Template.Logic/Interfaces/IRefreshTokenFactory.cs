namespace Template.Logic.Interfaces
{
    public interface IRefreshTokenFactory
    {
        string GenerateToken(int size = 32);
    }
}