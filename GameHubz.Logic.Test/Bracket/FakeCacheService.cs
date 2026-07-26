using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.Common.Interfaces;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// Fully working in-memory <see cref="ICacheService"/> for tests. Behaves like a real cache
    /// (get-after-set returns the stored value, remove evicts) so that the BracketService caching
    /// branches are exercised the same way they are in production, just without Redis.
    /// </summary>
    internal sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> store = new();

        public int SetCount { get; private set; }
        public int RemoveCount { get; private set; }

        public Task<T?> GetAsync<T>(string key)
        {
            if (store.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            store[key] = value;
            SetCount++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            store.Remove(key);
            RemoveCount++;
            return Task.CompletedTask;
        }

        // Counters live in the same store as everything else here; the real implementation keeps
        // them as plain Redis strings so RemoveAsync resets them, and that holds in-memory too.
        public Task<long> IncrementAsync(string key, TimeSpan window)
        {
            long next = (store.TryGetValue(key, out var existing) && existing is long current ? current : 0) + 1;

            store[key] = next;

            return Task.FromResult(next);
        }

        public Task<long> GetCounterAsync(string key)
        {
            return Task.FromResult(store.TryGetValue(key, out var value) && value is long counter ? counter : 0);
        }

        public Task RemoveCounterAsync(string key)
        {
            store.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern)
        {
            int star = pattern.IndexOf('*');
            if (star < 0)
            {
                store.Remove(pattern);
                return Task.CompletedTask;
            }

            string prefix = pattern.Substring(0, star);
            foreach (var key in store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                store.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}
