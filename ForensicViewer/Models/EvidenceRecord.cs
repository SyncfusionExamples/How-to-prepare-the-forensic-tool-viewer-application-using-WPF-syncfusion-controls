namespace ForensicViewer.Models
{
    public enum EvidenceType { Call, Message, BrowserHistory, File, AppArtifact, Account, Contact }
    public enum Direction { None, Incoming, Outgoing }
    public enum RecoveryStatus { Recovered, Deleted, Partial }

    /// <summary>
    /// Single forensic evidence record (SRS Section 8 data model).
    /// </summary>
    public class EvidenceRecord
    {
        public Guid RecordId { get; set; } = Guid.NewGuid();
        public string CaseId { get; set; }
        public string DeviceName { get; set; }
        public string Application { get; set; }
        public EvidenceType EvidenceType { get; set; }
        public Direction Direction { get; set; }
        public string Counterparty { get; set; }
        public DateTime Timestamp { get; set; }
        public string Content { get; set; }
        public long SizeBytes { get; set; }
        public RecoveryStatus RecoveryStatus { get; set; }
        public string SourceInfo { get; set; }
        public string HashMD5 { get; set; }
        public string HashSHA256 { get; set; }

        public string SizeDisplay => SizeBytes <= 0 ? "" : FormatSize(SizeBytes);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1 << 20) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1 << 10) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
