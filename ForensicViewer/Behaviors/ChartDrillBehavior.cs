using ForensicViewer.ViewModels;
using Microsoft.Xaml.Behaviors;
using Syncfusion.UI.Xaml.Charts;
using System.Windows;

namespace ForensicViewer.Behaviors
{
    /// <summary>
    /// Turns a chart data-point click into a cross-filter on the ViewModel (FR-09).
    /// Source = "EvidenceType" | "Application" | "Counterparty" for categorical charts;
    /// timeline buckets are detected from the item type.
    ///
    /// Crash guard: Syncfusion's AccumulationSeriesBase (doughnut) keeps a stale
    /// SelectedIndex when the filter rebuild shrinks the bound collection; the NEXT
    /// click then throws ArgumentOutOfRangeException in OnResetSegment. We reset
    /// selection in BeforeFiltersApplied — raised while the old segments are still
    /// valid — and deliberately do NOT re-select afterwards, because re-selecting
    /// against a stale/async-regenerating segment list recreates the same crash.
    /// With a drill active the chart typically shows a single slice, so no
    /// highlight restore is needed.
    /// </summary>
    public class ChartDrillBehavior : Behavior<SfChart>
    {
        private MainViewModel _vm;

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(string), typeof(ChartDrillBehavior));

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += OnLoaded;
            AssociatedObject.SelectionChanging += OnSelectionChanging;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.SelectionChanging -= OnSelectionChanging;
            if (_vm != null) _vm.BeforeFiltersApplied -= ResetSelection;
            base.OnDetaching();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm != null || AssociatedObject.DataContext is not MainViewModel vm) return;
            _vm = vm;
            // Fires at the START of ApplyFilters, before bound collections mutate.
            _vm.BeforeFiltersApplied += ResetSelection;
        }

        /// <summary>Clear selection while the current segment list is still valid.</summary>
        private void ResetSelection()
        {
            foreach (var series in AssociatedObject.Series)
            {
                // Guarded and swallowed: chart internals may already be mid-reset
                // when the filter comes from another tab's interaction.
                try
                {
                    if (series is AccumulationSeriesBase acc && acc.SelectedIndex >= 0)
                        acc.SelectedIndex = -1;
                }
                catch { /* stale chart state must never break filtering */ }
            }
        }

        private void OnSelectionChanging(object sender, ChartSelectionChangingEventArgs e)
        {
            if (!e.IsDataPointSelection || e.SelectedSegment == null) return;
            if (AssociatedObject.DataContext is not MainViewModel vm) return;

            // Deferred: mutating bound collections inside the chart's own event froze the app.
            var item = e.SelectedSegment.Item;
            if (item is TimeBucket bucket)
            {
                var day = bucket.Time;
                AssociatedObject.Dispatcher.BeginInvoke(() => vm.ToggleTimelineDay(day));
            }
            else if (item is NameValuePair pair)
            {
                var source = Source;
                var name = pair.Name;
                AssociatedObject.Dispatcher.BeginInvoke(() => vm.SetDrillFilter(source, name));
            }
        }
    }
}
