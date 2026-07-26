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

        // Atomic counter for abuse throttling (failed logins, reset-code sends). Returns the value
        // AFTER this increment. The window TTL is applied once, when the key is first created, so
        // the window runs from the first hit instead of sliding forward with every attempt.
        // Counter keys are a separate family from Get/SetAsync keys — don't mix the two on one key.
        Task<long> IncrementAsync(string key, TimeSpan window);

        // Current value of an IncrementAsync counter; 0 when the key has expired or never existed.
        Task<long> GetCounterAsync(string key);
    }
}