using System;
using System.Collections.Generic;
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
            var sampler = new ActivitySampler(start, end);

            for (int i = 0; i < totalRecords; i++)
            {
                int roll = random.Next(100);
                int contactIdx = random.Next(Contacts.Length);
                var rec = new EvidenceRecord
                {
                    CaseId = "CASE-2026-001",
                    DeviceName = Devices[random.Next(Devices.Length)],
                    Timestamp = sampler.Next(random),
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

        /// <summary>
        /// Shapes timestamps so the Activity Heatmap shows every band (below 10 ... 400 per cell):
        /// days are assigned to activity tiers and hours follow a business-hours or peak profile.
        /// Counts are per 50K records (scale proportionally for other volumes).
        /// </summary>
        private sealed class ActivitySampler
        {
            // (days, records per day, usePeakProfile)
            // Tuned so every (day,hour) cell lands INSIDE a legend band:
            // Heavy peak ~500/cell (clear of 400), Busy peak ~250/cell (inside 201-300),
            // and nothing falls in the unlabeled 301-399 gap.
            private static readonly (int Days, double PerDay, bool Peak)[] Tiers =
            {
                (5, 2500, true),   // Heavy       peak hours ~500 / cell
                (5, 1250, true),   // Busy        peak hours ~250 / cell
                (8, 1800, false),  // Moderate    ~140 / cell
                (8, 800,  false),  // Light       ~65  / cell
                (20, 300, false),  // Low         ~25  / cell
            };
            private const double QuietPerDay = 40;  // remaining days: below 10 / cell

            private static readonly double[] NormalProfile = BuildProfile(false);
            private static readonly double[] PeakProfile = BuildProfile(true);

            private readonly DateTime _start;
            private readonly double[] _dayCum;
            private readonly bool[] _dayPeak;

            public ActivitySampler(DateTime start, DateTime end)
            {
                _start = start.Date;
                int days = (int)(end.Date - _start).TotalDays + 1;
                var perDay = new double[days];
                _dayPeak = new bool[days];
                for (int i = 0; i < days; i++) perDay[i] = QuietPerDay;

                var order = new List<int>();
                for (int i = 0; i < days; i++) order.Add(i);
                var rnd = new Random(7);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                int k = 0;
                foreach (var tier in Tiers)
                    for (int n = 0; n < tier.Days && k < order.Count; n++, k++)
                    {
                        perDay[order[k]] = tier.PerDay;
                        _dayPeak[order[k]] = tier.Peak;
                    }

                _dayCum = new double[days];
                double sum = 0;
                for (int i = 0; i < days; i++) { sum += perDay[i]; _dayCum[i] = sum; }
            }

            public DateTime Next(Random random)
            {
                double r = random.NextDouble() * _dayCum[^1];
                int day = Array.BinarySearch(_dayCum, r);
                if (day < 0) day = ~day;
                if (day >= _dayCum.Length) day = _dayCum.Length - 1;

                var profile = _dayPeak[day] ? PeakProfile : NormalProfile;
                double h = random.NextDouble();
                int hour = 0;
                while (hour < 23 && h > profile[hour]) hour++;

                return _start.AddDays(day).AddHours(hour)
                             .AddSeconds(random.Next(3600));
            }

            private static double[] BuildProfile(bool peak)
            {
                var w = new double[24];
                for (int h = 0; h < 24; h++)
                {
                    if (peak) w[h] = (h >= 10 && h <= 13) ? 20 : 1;
                    else w[h] = h < 7 ? 1 : h < 9 ? 4 : h < 18 ? 12 : h < 22 ? 6 : 2;
                }
                double total = 0; foreach (var x in w) total += x;
                var cum = new double[24]; double acc = 0;
                for (int h = 0; h < 24; h++) { acc += w[h] / total; cum[h] = acc; }
                return cum;
            }
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
