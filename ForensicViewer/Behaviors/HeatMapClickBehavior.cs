using ForensicViewer.ViewModels;
using Microsoft.Xaml.Behaviors;
using Syncfusion.UI.Xaml.HeatMap;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ForensicViewer.Behaviors
{
    /// <summary>SfHeatMap raises no click events: hit-test the cell and toggle it on the ViewModel.</summary>
    public class HeatMapClickBehavior : Behavior<SfHeatMap>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewMouseLeftButtonDown += OnMouseDown;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnMouseDown;
            base.OnDetaching();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (AssociatedObject.DataContext is not MainViewModel vm) return;
            try
            {
                var hit = VisualTreeHelper.HitTest(AssociatedObject, e.GetPosition(AssociatedObject));
                DependencyObject cur = hit?.VisualHit;
                while (cur != null && cur != AssociatedObject)
                {
                    if (cur is HeatMapCell cell)
                    {
                        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                        var row = GetCellInfoValue(cell, "RowHeader", flags);
                        var col = GetCellInfoValue(cell, "ColumnHeader", flags);
                        if (row == null || col == null) return;
                        var match = vm.FindHeatMapCell(row, col);
                        if (match != null) AssociatedObject.Dispatcher.BeginInvoke(() => vm.ToggleHeatMapCell(match));
                        return;
                    }
                    cur = VisualTreeHelper.GetParent(cur);
                }
            }
            catch (Exception ex) { BehaviorLog.Write("Heatmap hit-test", ex); }
        }

        private static string GetCellInfoValue(HeatMapCell cell, string propertyName, BindingFlags flags)
        {
            var cellInfo = cell.GetType().GetProperty(propertyName, flags)?.GetValue(cell);
            if (cellInfo == null) return null;
            var value = cellInfo.GetType().GetProperty("Value", flags)?.GetValue(cellInfo)
                     ?? cellInfo.GetType().GetProperty("Content")?.GetValue(cellInfo);
            return value?.ToString();
        }
    }

    internal static class BehaviorLog
    {
        public static void Write(string title, Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(App.LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BEHAVIOR: {title}\n{ex}\n\n");
            }
            catch { }
        }
    }
}
