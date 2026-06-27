namespace GameHubz.Common.Interfaces
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key);

        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

        Task RemoveAsync(string key);

        // Deletes every key matching a Redis glob-style pattern (e.g. "tournaments:hub:abc:*").
        // Use only with bounded key families — patterns that match thousands of keys are slow.
        Task RemoveByPatternAsync(string pattern);
    }
}