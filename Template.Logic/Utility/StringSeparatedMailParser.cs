using System.Net.Mail;

namespace Template.Logic.Utility
{
    public class StringSeparatedMailParser
    {
        internal static List<string> GetEmailsFromString(string combinedEmails)
        {
            if (string.IsNullOrWhiteSpace(combinedEmails))
            {
                return new();
            }

            List<string> splits = ParseEmails(combinedEmails);

            List<string> emails = new();

            foreach (var email in splits)
            {
                try
                {
                    // Validate email
                    _ = new MailAddress(email);

                    emails.Add(email);
                }
                catch
                {
                    // Skip invalid emails
                    // Left empty on purpose
                }
            }

            return emails;
        }

        private static List<string> ParseEmails(string combinedEmails)
        {
            string[] separators = new string[] { " ", ",", ";" };

            List<string> splits = new()
            {
                combinedEmails
            };

            foreach (string separator in separators)
            {
                List<string> splitTemp = new();

                foreach (string split in splits)
                {
                    splitTemp.AddRange(split.Split(separator));
                }

                splits = splitTemp;
            }

            return splits;
        }
    }
}