using ForensicViewer.Models;
using ForensicViewer.Services;
using ForensicViewer.ViewModels;
using System.Windows;

namespace ForensicViewer
{
    /// <summary>
    /// View: no event handlers. Interaction with Syncfusion controls (chart drill, range navigator,
    /// heatmap click, diagram rendering) lives in Behaviors/. This file only creates the ViewModel
    /// and provides chart bitmaps for PDF export.
    /// </summary>
    public partial class MainWindow : Window, IChartSnapshotProvider
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel(recordCount: 50_000) { SnapshotProvider = this };
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
