using System.Security.Cryptography;
using System.Text;

namespace Template.Logic.Crypto
{
    public class AesCrypter
    {
#pragma warning disable CA1822 // Mark members as static

        public string Decrypt(string key, string passwordNonce, string cipherText)
#pragma warning restore CA1822 // Mark members as static
        {
            byte[] key2 = Encoding.UTF8.GetBytes(key);
            byte[] iv = Encoding.UTF8.GetBytes(passwordNonce);

            using var aes = Aes.Create();

            if (aes == null)
            {
                throw new Exception("Aes is null");
            }

            byte[] buffer = Convert.FromBase64String(cipherText);
            using MemoryStream memoryStream = new(buffer);

            using CryptoStream cryptStream = new(
                   memoryStream,
                   aes.CreateDecryptor(key2, iv),
                   CryptoStreamMode.Read);

            using StreamReader streamReader = new(cryptStream);

            string decryptedValue = streamReader.ReadToEnd();

            streamReader.Close();
            memoryStream.Close();

            return decryptedValue.Trim();
        }

#pragma warning disable CA1822 // Mark members as static

        public string Encrypt(string key, string passwordNonce, string value)
#pragma warning restore CA1822 // Mark members as static
        {
            using var aes = Aes.Create();

            if (aes == null)
            {
                throw new Exception("Aes is null");
            }

            byte[] key2 = Encoding.UTF8.GetBytes(key);
            byte[] iv = Encoding.UTF8.GetBytes(passwordNonce);

            using MemoryStream memoryStream = new();

            using CryptoStream cryptoStream = new(
                    memoryStream,
                    aes.CreateEncryptor(key2, iv),
                    CryptoStreamMode.Write);

            using StreamWriter streamWriter = new(cryptoStream);
            streamWriter.WriteLine(value);

            streamWriter.Close();
            cryptoStream.Close();

            byte[] byteResult = memoryStream.ToArray();
            string encrypted = Convert.ToBase64String(byteResult);

            memoryStream.Close();

            return encrypted;
        }
    }
}