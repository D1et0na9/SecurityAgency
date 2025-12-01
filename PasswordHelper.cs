using System.Security.Cryptography;
using System.Text;

namespace SecurityAgencysApp
{
    public static class PasswordHelper
    {
        public static string ComputeSha256Hash(string rawData)
        {
            // Защита от null и нормализация (убираем лид/трейл пробелы)
            var input = (rawData ?? string.Empty).Trim();

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2")); // только нижний регистр
                return builder.ToString();
            }
        }
    }
}