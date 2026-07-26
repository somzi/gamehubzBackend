using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    public class RedisCacheService : ICacheService
    {
        // Must match the InstanceName configured for AddStackExchangeRedisCache — that prefix is
        // added by IDistributedCache when writing keys, so RemoveByPatternAsync needs to add it
        // when SCAN-ing through the multiplexer (which sees raw Redis keys with no prefix added).
        private const string InstanceName = "GameHubz_";

        private readonly IDistributedCache _cache;
        private readonly IConnectionMultiplexer _multiplexer;

        public RedisCacheService(IDistributedCache cache, IConnectionMultiplexer multiplexer)
        {
            _cache = cache;
            _multiplexer = multiplexer;
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var data = await _cache.GetStringAsync(key);

            if (string.IsNullOrEmpty(data))
                return default;

            return JsonSerializer.Deserialize<T>(data);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            var options = new DistributedCacheEntryOptions
            {
                // Ako ne pošalješ vreme, default je 10 minuta
                AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(10)
            };

            var jsonData = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, jsonData, options);
        }

        public async Task RemoveAsync(string key)
        {
            await _cache.RemoveAsync(key);
        }

        // Counter keys are written as plain Redis strings through the multiplexer (INCR), not as
        // the hash IDistributedCache uses for Get/SetAsync — so a key must be used with one pair of
        // methods or the other, never both. The InstanceName prefix is applied by hand here to match
        // what IDistributedCache adds, which keeps RemoveAsync able to reset these counters.
        public async Task<long> IncrementAsync(string key, TimeSpan window)
        {
            var db = _multiplexer.GetDatabase();
            string prefixedKey = InstanceName + key;

            long value = await db.StringIncrementAsync(prefixedKey);

            // Only the first hit sets the TTL. Refreshing it on every increment would let a steady
            // stream of attempts push the expiry out forever, so the counter would never reset.
            if (value == 1)
            {
                await db.KeyExpireAsync(prefixedKey, window);
            }

            return value;
        }

        public async Task<long> GetCounterAsync(string key)
        {
            var db = _multiplexer.GetDatabase();

            RedisValue value = await db.StringGetAsync(InstanceName + key);

            return value.TryParse(out long parsed) ? parsed : 0;
        }

        public async Task RemoveCounterAsync(string key)
        {
            await _multiplexer.GetDatabase().KeyDeleteAsync(InstanceName + key);
        }

        // SCAN-based pattern delete. We iterate every (non-replica) endpoint to cover cluster
        // setups, collect matching keys, then batch-delete in one round-trip per server. SCAN
        // is non-blocking on the Redis side; safe to use during normal operation.
        public async Task RemoveByPatternAsync(string pattern)
        {
            string prefixedPattern = InstanceName + pattern;
            var db = _multiplexer.GetDatabase();

            foreach (var endpoint in _multiplexer.GetEndPoints())
            {
                var server = _multiplexer.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica) continue;

                var keys = new List<RedisKey>();
                await foreach (var key in server.KeysAsync(pattern: prefixedPattern))
                {
                    keys.Add(key);
                }

                if (keys.Count > 0)
                {
                    await db.KeyDeleteAsync(keys.ToArray());
                }
            }
        }
    }
}