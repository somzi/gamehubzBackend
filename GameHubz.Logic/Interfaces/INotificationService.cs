namespace GameHubz.Logic.Interfaces
{
    public interface INotificationService
    {
        Task SendToOneAsync(string pushToken, string title, string body, object? data = null);

        Task SendToManyAsync(IEnumerable<string> pushTokens, string title, string body, object? data = null);
    }
}
