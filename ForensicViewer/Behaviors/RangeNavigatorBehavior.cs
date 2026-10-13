using ForensicViewer.ViewModels;
using Microsoft.Xaml.Behaviors;
using Syncfusion.UI.Xaml.Charts;
using System.ComponentModel;
using System.Windows;

namespace ForensicViewer.Behaviors
{
    /// <summary>
    /// Feeds the SfDateTimeRangeNavigator a static day list and forwards its selected
    /// range to the ViewModel (SelectedData has no event, so a property descriptor is used).
    /// </summary>
    public class RangeNavigatorBehavior : Behavior<SfDateTimeRangeNavigator>
    {
        private MainViewModel _vm;
        private bool _applying;
        private DependencyPropertyDescriptor _descriptor;

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += OnLoaded;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Loaded -= OnLoaded;
            _descriptor?.RemoveValueChanged(AssociatedObject, OnSelectedDataChanged);
            base.OnDetaching();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm != null || AssociatedObject.DataContext is not MainViewModel vm) return;
            _vm = vm;

            // Static full-range day list: never the live TimelineCounts (re-entrancy loop).
            AssociatedObject.ItemsSource = vm.BuildNavigatorDays();
            var type = typeof(SfDateTimeRangeNavigator);
            _descriptor = DependencyPropertyDescriptor.FromName("SelectedData", type, type);
            _descriptor?.AddValueChanged(AssociatedObject, OnSelectedDataChanged);
        }

        private void OnSelectedDataChanged(object sender, EventArgs e)
        {
            if (_vm == null || _applying) return;
            if (AssociatedObject.SelectedData is not System.Collections.IEnumerable items) return;

            DateTime? start = null, end = null;
            foreach (var it in items)
            {
                if (it is TimeBucket b)
                {
                    if (start == null || b.Time < start) start = b.Time;
                    if (end == null || b.Time >= end) end = b.Time;
                }
            }
            if (start == null) return;
            if (_vm.TimelineStart == start && _vm.TimelineEnd == end?.AddDays(1)) return;

            _applying = true;
            AssociatedObject.Dispatcher.BeginInvoke(() =>
            {
                try { _vm.SetTimelineRange(start, end); }
                finally { _applying = false; }
            });
        }
    }
}
