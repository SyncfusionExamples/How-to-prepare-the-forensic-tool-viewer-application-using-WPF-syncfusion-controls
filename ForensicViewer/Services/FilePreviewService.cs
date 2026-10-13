using ForensicViewer.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace ForensicViewer.Services
{
    /// <summary>
    /// Extension-based MIME type + category detection and thumbnail loading
    /// for recovered file artifacts. Pure lookup logic — no UI dependencies.
    /// </summary>
    public static class FilePreviewService
    {
        private static readonly Dictionary<string, string> MimeByExt = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
            [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".tiff"] = "image/tiff",
            [".webp"] = "image/webp",
            // Videos
            [".mp4"] = "video/mp4", [".avi"] = "video/x-msvideo", [".mkv"] = "video/x-matroska",
            [".mov"] = "video/quicktime", [".wmv"] = "video/x-ms-wmv", [".webm"] = "video/webm",
            // Audio
            [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".aac"] = "audio/aac",
            [".m4a"] = "audio/mp4",
            // Documents
            [".pdf"] = "application/pdf", [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".txt"] = "text/plain", [".rtf"] = "application/rtf", [".csv"] = "text/csv",
            [".html"] = "text/html", [".xml"] = "application/xml", [".json"] = "application/json",
            // Archives
            [".zip"] = "application/zip", [".rar"] = "application/vnd.rar",
            [".7z"] = "application/x-7z-compressed", [".tar"] = "application/x-tar",
            [".gz"] = "application/gzip",
        };

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

        private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
            { ".zip", ".rar", ".7z", ".tar", ".gz" };

        private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
            { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
              ".txt", ".rtf", ".csv", ".html", ".xml", ".json" };

        public static string DetermineMimeType(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? "");
            return MimeByExt.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
        }

        public static MediaCategory GetMediaCategory(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? "");
            if (ImageExts.Contains(ext)) return MediaCategory.Images;
            if (VideoExts.Contains(ext)) return MediaCategory.Videos;
            if (ArchiveExts.Contains(ext)) return MediaCategory.Archives;
            if (DocumentExts.Contains(ext)) return MediaCategory.Documents;
            return MediaCategory.Other;
        }

        /// <summary>Loads a decode-on-demand thumbnail for image files; null otherwise / on error.</summary>
        public static BitmapImage LoadThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) ||
                GetMediaCategory(Path.GetFileName(filePath)) != MediaCategory.Images ||
                !File.Exists(filePath))
                return null;

            try
            {
                return new BitmapImage(new Uri(filePath, UriKind.Absolute))
                {
                    DecodePixelWidth = 320,
                    CacheOption = BitmapCacheOption.OnLoad
                };
            }
            catch { return null; }
        }
    }
}
