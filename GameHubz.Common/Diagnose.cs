using System.Diagnostics;

namespace GameHubz.Common
{
    public class Diagnose
    {
        private readonly List<DebugInfo> debugInfos;

        private readonly Random random;

        private int nesting;

        public Diagnose()
        {
            this.random = new();
            this.debugInfos = new();
            this.nesting = 0;
        }

        public T MeasureSync<T>(Func<T> func, string description)
        {
            if (!Debugger.IsAttached)
            {
                return func();
            }

            this.nesting++;

            var key = this.RandomString(7);
            this.AddStart(description, key);

            OutputStart(description, key);

            Stopwatch stopwatch = new();
            stopwatch.Start();
            T result = func();
            stopwatch.Stop();
            Output(description, stopwatch, key);

            this.AddEnd(description, key, stopwatch);

            this.nesting--;

            return result;
        }

        public async Task<T> Measure<T>(Func<Task<T>> func, string description)
        {
            if (!Debugger.IsAttached)
            {
                return await func();
            }

            this.nesting++;
            var key = this.RandomString(7);

            this.AddStart(description, key);
            OutputStart(description, key);
            Stopwatch stopwatch = new();
            stopwatch.Start();
            T result = await func();
            stopwatch.Stop();
            Output(description, stopwatch, key);

            this.AddEnd(description, key, stopwatch);

            this.nesting--;

            return result;
        }

        public async Task MeasureVoid(Func<Task> func, string description)
        {
            if (!Debugger.IsAttached)
            {
                await func();
            }

            this.nesting++;

            var key = this.RandomString(7);

            this.AddStart(description, key);

            OutputStart(description, key);
            Stopwatch stopwatch = new();
            stopwatch.Start();
            await func();
            stopwatch.Stop();
            Output(description, stopwatch, key);

            this.AddEnd(description, key, stopwatch);

            this.nesting--;
        }

        public void OutputCollectedInfo()
        {
            this.debugInfos.ForEach(x => Debug.WriteLine(x));
        }

        public string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[this.random.Next(s.Length)]).ToArray());
        }

        private void AddStart(string description, string key)
        {
            this.debugInfos.Add(new DebugInfo()
            {
                Nesting = nesting,
                Description = $"START {description}",
                Key = key,
                Time = -1
            });
        }

        private void AddEnd(string description, string key, Stopwatch stopwatch)
        {
            this.debugInfos.Add(new DebugInfo()
            {
                Nesting = nesting,
                Description = $"END {description}",
                Key = key,
                Time = stopwatch.ElapsedMilliseconds
            });
        }

        private static void OutputStart(string description, string key)
        {
            Debug.WriteLine("==================================================");
            Debug.WriteLine("==================================================");
            Debug.WriteLine($"STOPWATCH START: {key}, {description}");
            Debug.WriteLine("==================================================");
            Debug.WriteLine("==================================================");
        }

        private static void Output(string description, Stopwatch stopwatch, string key)
        {
            Debug.WriteLine("==================================================");
            Debug.WriteLine("==================================================");
            Debug.WriteLine($"STOPWATCH END: {key} {description}: {stopwatch.ElapsedMilliseconds}ms");
            Debug.WriteLine("==================================================");
            Debug.WriteLine("==================================================");
        }

        private class DebugInfo
        {
            public DebugInfo()
            {
                this.Description = "";
                this.Key = "";
            }

            public string Description { get; set; }
            public long Time { get; set; }
            public string Key { get; set; }
            public int Nesting { get; set; }

            public override string ToString()
            {
                var time = this.Time < 0 ? "" : $"{this.Time}ms";
                var indetation = new string(Enumerable.Repeat(' ', this.Nesting).ToArray());
                return $"{indetation}{this.Description} ({this.Key}): {time}";
            }
        }
    }
}
