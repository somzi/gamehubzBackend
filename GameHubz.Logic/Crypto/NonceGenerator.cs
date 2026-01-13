namespace GameHubz.Logic.Crypto
{
    public static class NonceGenerator
    {
        public const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()_-+=?:;|{}[]<>,.~";

        public static string GetNew(int length = 16)
        {
            int seed = Guid.NewGuid().ToString()
                .Select(x => (int)x)
                .Sum() * DateTime.Now.Millisecond;

            Random random = new(seed);

            string nonce = "";

            while (nonce.Length < length)
            {
                int randomIndex = random.Next(0, Characters.Length - 1);
                nonce += Characters[randomIndex];
            }

            return nonce;
        }
    }
}
