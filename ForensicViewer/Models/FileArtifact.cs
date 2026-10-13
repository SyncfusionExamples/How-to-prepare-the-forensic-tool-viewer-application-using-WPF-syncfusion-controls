namespace ForensicViewer.Models
{
    /// <summary>High-level media category used by the File & Media Analysis tab.</summary>
    public enum MediaCategory { Images, Videos, Documents, Archives, Other }

    /// <summary>
    /// A recovered file artifact (image, video, document, archive ...) —
    /// SRS Section 8 data model extension for file-level analysis.
    /// </summary>
    public class FileArtifact
    {
        public Guid FileId { get; set; } = Guid.NewGuid();
        public string CaseId { get; set; }
        public string FileName { get; set; }
        public string FileExtension { get; set; }        // ".pdf" (lowercase, includes dot)
        public string FileType { get; set; }             // MIME type, e.g. "application/pdf"
        public string FilePath { get; set; }            // original path on the evidence device
        public long FileSizeBytes { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public RecoveryStatus RecoveryStatus { get; set; }
        public string DeviceName { get; set; }
        public string Application { get; set; }
        public string HashMD5 { get; set; }
        public string HashSHA256 { get; set; }

        public MediaCategory Category { get; set; }
        public string CategoryDisplay => Category.ToString();

        public string SizeDisplay => FileSizeBytes <= 0
            ? ""
            : FileSizeBytes >= 1L << 30 ? $"{FileSizeBytes / 1073741824.0:F2} GB"
            : FileSizeBytes >= 1L << 20 ? $"{FileSizeBytes / 1048576.0:F1} MB"
            : FileSizeBytes >= 1L << 10 ? $"{FileSizeBytes / 1024.0:F1} KB"
            : $"{FileSizeBytes} B";

        /// <summary>True when the tab can render a live preview for this file type.</summary>
        public bool IsPreviewable =>
            Category == MediaCategory.Images ||
            Category == MediaCategory.Documents && FileExtension == ".pdf";
    }
}
