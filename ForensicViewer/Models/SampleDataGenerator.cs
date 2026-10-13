using System.Security.Cryptography;
using System.Text;

namespace ForensicViewer.Models
{
    /// <summary>
    /// Generates synthetic forensic evidence records for the POC demo.
    /// Deterministic seed ensures repeatable demos.
    /// </summary>
    public static class SampleDataGenerator
    {
        private static readonly string[] Contacts =
        {
            "Alex Mercer", "Riya Sharma", "John Doe", "Maria Lopez", "David Kim",
            "Sarah Chen", "Ravi Patel", "Emma Wilson", "Omar Hassan", "Ling Wei",
            "Nina Petrov", "Carlos Mendez", "Fatima Ali", "Tom Becker", "Ana Silva"
        };

        private static readonly string[] Phones =
        {
            "+1-555-0101", "+1-555-0102", "+1-555-0103", "+1-555-0104", "+1-555-0105",
            "+1-555-0106", "+1-555-0107", "+1-555-0108", "+1-555-0109", "+1-555-0110",
            "+1-555-0111", "+1-555-0112", "+1-555-0113", "+1-555-0114", "+1-555-0115"
        };

        private static readonly string[] Devices = { "iPhone 14 (Suspect A)", "Samsung S23 (Suspect B)", "Dell Laptop XP-77", "USB Drive KING-16GB" };
        private static readonly string[] Urls =
        {
            "https://mail.google.com/u/0/inbox", "https://bank.example.com/login",
            "https://shopping.example.org/checkout", "https://news.example.net/article/42",
            "https://social.example.app/profile/123", "https://search.example.com?q=test"
        };
        private static readonly string[] FilePathList =
        {
            "/data/media/IMG_2043.jpg", "/data/media/VID_0092.mp4", "/docs/report_final.docx",
            "/downloads/archive.zip", "/data/media/IMG_0917.png", "/docs/financials_2024.xlsx"
        };
        private static readonly string[] MessageBodies =
        {
            "Meet me at the usual place @ 9", "Did you get the files?", "Call me when free",
            "Transfer done, check balance", "We need to talk about the delivery",
            "Here are the documents you asked", "Everything is set for tomorrow",
            "Don't forget our agreement", "Call forward confirmation received", "See you tomorrow"
        };
        private static readonly string[] AccountNames =
        {
            "john.doe@gmail.com", "j.doe_work@outlook.com", "user_92@protonmail.com",
            "account_1@yahoo.com", "primary@icloud.com"
        };

        public static List<EvidenceRecord> Generate(int totalRecords)
        {
            var records = new List<EvidenceRecord>(totalRecords);
            var random = new Random(42);
            var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 10, 10, 23, 59, 59, DateTimeKind.Utc);
            long rangeSeconds = (long)(end - start).TotalSeconds;

            for (int i = 0; i < totalRecords; i++)
            {
                int roll = random.Next(100);
                int contactIdx = random.Next(Contacts.Length);
                var rec = new EvidenceRecord
                {
                    CaseId = "CASE-2026-001",
                    DeviceName = Devices[random.Next(Devices.Length)],
                    Timestamp = start.AddSeconds(random.Next((int)Math.Min(rangeSeconds, int.MaxValue - 1))),
                    SourceInfo = $"/evidence/orig/artifact_{i % 99:00}.db",
                    Counterparty = $"{Contacts[contactIdx]} ({Phones[contactIdx]})"
                };

                if (roll < 35)
                {
                    rec.EvidenceType = EvidenceType.Call;
                    rec.Application = "Call Log";
                    rec.Direction = random.Next(2) == 0 ? Direction.Incoming : Direction.Outgoing;
                    rec.Content = $"Call duration: {random.Next(5, 2400)} sec";
                }
                else if (roll < 70)
                {
                    rec.EvidenceType = EvidenceType.Message;
                    var msgApps = new[] { "WhatsApp", "SMS", "Telegram" };
                    rec.Application = msgApps[random.Next(msgApps.Length)];
                    rec.Direction = random.Next(2) == 0 ? Direction.Incoming : Direction.Outgoing;
                    rec.Content = MessageBodies[random.Next(MessageBodies.Length)];
                }
                else if (roll < 82)
                {
                    rec.EvidenceType = EvidenceType.BrowserHistory;
                    var browsers = new[] { "Chrome", "Edge" };
                    rec.Application = browsers[random.Next(browsers.Length)];
                    rec.Direction = Direction.None;
                    rec.Counterparty = "Web";
                    rec.Content = Urls[random.Next(Urls.Length)];
                }
                else if (roll < 94)
                {
                    rec.EvidenceType = EvidenceType.File;
                    rec.Application = "Files";
                    rec.Direction = Direction.None;
                    rec.Counterparty = "Device";
                    rec.Content = FilePathList[random.Next(FilePathList.Length)];
                    rec.SizeBytes = random.Next(5_000, 250_000_000);
                }
                else if (roll < 98)
                {
                    rec.EvidenceType = EvidenceType.Account;
                    rec.Application = "Gmail";
                    rec.Direction = Direction.None;
                    rec.Counterparty = "Account";
                    rec.Content = AccountNames[random.Next(AccountNames.Length)];
                }
                else
                {
                    rec.EvidenceType = EvidenceType.Contact;
                    rec.Application = "Contacts";
                    rec.Direction = Direction.None;
                    rec.Content = $"Contact record for {Contacts[contactIdx]}";
                }

                rec.RecoveryStatus = (RecoveryStatus)random.Next(3);
                rec.HashMD5 = ComputeHash(rec, "MD5");
                rec.HashSHA256 = ComputeHash(rec, "SHA256");
                records.Add(rec);
            }
            return records;
        }

        private static string ComputeHash(EvidenceRecord rec, string algorithm)
        {
            string raw = $"{rec.RecordId}|{rec.CaseId}|{rec.DeviceName}|{rec.Application}|{rec.EvidenceType}|{rec.Content}|{rec.Timestamp:O}";
            using var algo = algorithm == "MD5" ? MD5.Create() : (HashAlgorithm)SHA256.Create();
            var hash = algo.ComputeHash(Encoding.UTF8.GetBytes(raw));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
