using ForensicViewer.Models;
using ForensicViewer.Services;
using ForensicViewer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PointF = System.Drawing.PointF;

namespace ForensicViewer
{
    /// <summary>
    /// View: no business-logic event handlers. Interaction with Syncfusion controls (chart drill,
    /// range navigator, heatmap click, diagram rendering) lives in Behaviors/. This file creates
    /// the ViewModel, provides chart bitmaps for PDF export and switches the File & Media
    /// preview panel between the PDF viewer / image preview / metadata fallback.
    /// </summary>
    public partial class MainWindow : Window, IChartSnapshotProvider
    {
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel(recordCount: 50_000) { SnapshotProvider = this };
            vm.MediaAnalysis.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MediaAnalysisViewModel.SelectedFile))
                    Dispatcher.BeginInvoke(() => UpdateFilePreview(vm.MediaAnalysis.SelectedFile));
            };
            DataContext = vm;
        }

        // ---------------- File & Media preview panel (tab 5) ----------------

        private string _demoPdfPath;

        /// <summary>Grid re-selection safety net (clears preview when the selection is cleared from the UI).</summary>
        private void OnFileGridSelectionChanged(object sender, Syncfusion.UI.Xaml.Grid.GridSelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.MediaAnalysis.SelectedFile == null)
                UpdateFilePreview(null);
        }

        private void UpdateFilePreview(FileArtifact file)
        {
            if (file == null)
            {
                ShowPreviewPanel(PreviewPlaceholder);
                return;
            }

            switch (file.Category)
            {
                case MediaCategory.Documents when file.FileExtension == ".pdf":
                    FilePdfViewer.Load(_demoPdfPath ??= CreateDemoPdf(file));
                    ShowPreviewPanel(FilePdfViewer);
                    break;

                case MediaCategory.Images:
                    var img = CreateDemoImage(file);
                    if (img != null)
                    {
                        PreviewImage.Source = img;
                        ShowPreviewPanel(PreviewImage);
                    }
                    else ShowMeta(file);
                    break;

                default:
                    ShowMeta(file);
                    break;
            }
        }

        private void ShowPreviewPanel(UIElement panel)
        {
            var panels = new UIElement[] { PreviewPlaceholder, FilePdfViewer, PreviewImage, PreviewMeta };
            foreach (var p in panels) p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowMeta(FileArtifact file) =>
            PreviewMetaText.Text =
                $"{file.FileName}\n\nMIME type: {file.FileType}\nCategory: {file.CategoryDisplay}\n" +
                $"Size: {file.SizeDisplay} ({file.FileSizeBytes:N0} bytes)\n" +
                $"Recovery status: {file.RecoveryStatus}\nDevice: {file.DeviceName}\n" +
                $"Source app: {file.Application}\nCreated (UTC): {file.CreatedDate:yyyy-MM-dd HH:mm:ss}\n" +
                $"Modified (UTC): {file.ModifiedDate:yyyy-MM-dd HH:mm:ss}\n\n" +
                $"Original path: {file.FilePath}\n\nMD5:  {file.HashMD5}\nSHA-256:\n{file.HashSHA256}";

        /// <summary>Builds a small demo PDF (chart-like content) — sample artifacts are synthetic.</summary>
        private string CreateDemoPdf(FileArtifact file)
        {
            var path = Path.Combine(Path.GetTempPath(), $"forensicdemo_{file.FileId:N}.pdf");
            if (File.Exists(path)) return path;

            using var doc = new Syncfusion.Pdf.PdfDocument();
            var page = doc.Pages.Add();
            var g = page.Graphics;
            g.DrawString("Forensic Tool Viewer — File Artifact Extract",
                new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 16, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold),
                Syncfusion.Pdf.Graphics.PdfBrushes.Black, new PointF(0, 0));
            g.DrawString($"File: {file.FileName}\nCase: {file.CaseId}\nDevice: {file.DeviceName}\n" +
                         $"Size: {file.FileSizeBytes:N0} bytes\nStatus: {file.RecoveryStatus}\n" +
                         $"\nThis is a synthetic demo document generated for the POC — " +
                         "in production the recovered file bytes would be rendered here.",
                new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 11),
                Syncfusion.Pdf.Graphics.PdfBrushes.Black, new PointF(0, 40));
            using var ms = File.Create(path);
            doc.Save(ms);
            return path;
        }

        /// <summary>Generates a deterministic per-file demo image (no real media in sample data).</summary>
        private BitmapImage CreateDemoImage(FileArtifact file)
        {
            try
            {
                const int size = 512;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var rnd = new Random(file.FileId.GetHashCode());
                    var bg = new SolidColorBrush(Color.FromRgb((byte)rnd.Next(256), (byte)rnd.Next(256), 0x20));
                    dc.DrawRectangle(bg, null, new Rect(0, 0, size, size));
                    for (int i = 0; i < 40; i++)
                    {
                        dc.DrawEllipse(Brushes.White, null,
                            new Point(rnd.Next(size), rnd.Next(size)), 2 + rnd.Next(15), 2 + rnd.Next(15));
                    }
                    var tf = new FormattedText(file.FileName,
                        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 14, Brushes.White, 1.0);
                    dc.DrawText(tf, new Point(12, 12));
                    var tf2 = new FormattedText($"Demo image — {file.DeviceName}",
                        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 11, Brushes.White, 1.0);
                    dc.DrawText(tf2, new Point(12, 34));
                }
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
            catch { return null; }
        }

        public IReadOnlyList<ChartSnapshot> CaptureSnapshots()
        {
            var list = new List<ChartSnapshot>();
            foreach (var (title, chart) in new (string, FrameworkElement)[]
                     { ("Evidence by Type", ChartTypes), ("Evidence by Application", ChartApps) })
            {
                try
                {
                    var size = new Size(chart.ActualWidth, chart.ActualHeight);
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    chart.Measure(size);
                    chart.Arrange(new Rect(size));
                    rtb.Render(chart);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using var ms = new System.IO.MemoryStream();
                    encoder.Save(ms);
                    list.Add(new ChartSnapshot(title, ms.ToArray()));
                }
                catch { /* chart not rendered yet - skip */ }
            }
            return list;
        }
    }
}
