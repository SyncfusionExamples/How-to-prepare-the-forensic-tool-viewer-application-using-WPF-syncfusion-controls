using ForensicViewer.Models;
using ForensicViewer.ViewModels;
using Microsoft.Xaml.Behaviors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConnectorViewModel = Syncfusion.UI.Xaml.Diagram.ConnectorViewModel;
using IGraphInfo = Syncfusion.UI.Xaml.Diagram.IGraphInfo;
using ItemTappedEventArgs = Syncfusion.UI.Xaml.Diagram.ItemTappedEventArgs;
using NodeViewModel = Syncfusion.UI.Xaml.Diagram.NodeViewModel;
using SfDiagram = Syncfusion.UI.Xaml.Diagram.SfDiagram;
using TextAnnotationViewModel = Syncfusion.UI.Xaml.Diagram.TextAnnotationViewModel;

namespace ForensicViewer.Behaviors
{
    /// <summary>
    /// Hosts an SfDiagram inside a Grid and renders MainViewModel.Graph into it.
    /// Handles Reset View (ResetGraphViewRequested) and node taps (forwarded to the VM).
    /// </summary>
    public class GraphDiagramBehavior : Behavior<Grid>
    {
        private MainViewModel _vm;
        private SfDiagram _diagram;
        private readonly Dictionary<string, GraphNodeInfo> _nodeLookup = new();

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += OnLoaded;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Loaded -= OnLoaded;
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.ResetGraphViewRequested -= ResetGraphView;
            }
            base.OnDetaching();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm != null || AssociatedObject.DataContext is not MainViewModel vm) return;
            _vm = vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ResetGraphViewRequested += ResetGraphView;
            BuildDiagram();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.Graph))
                AssociatedObject.Dispatcher.BeginInvoke(RenderGraph);
        }

        private void BuildDiagram()
        {
            _diagram = new SfDiagram
            {
                // ZoomPan: scroll-zoom + pan. MultipleSelect is REQUIRED for ItemTappedEvent.
                Tool = Syncfusion.UI.Xaml.Diagram.Tool.ZoomPan | Syncfusion.UI.Xaml.Diagram.Tool.MultipleSelect,
                SelectorHandleDisplayMode = Syncfusion.UI.Xaml.Diagram.SelectorHandleDisplayMode.Compact,
                IsEnabled = true,
                Focusable = true
            };

            HideSelectorChrome();
            _diagram.Loaded += (s, e) => HideSelectorChrome();

            _diagram.LayoutManager = new Syncfusion.UI.Xaml.Diagram.Layout.LayoutManager
            {
                Layout = new Syncfusion.UI.Xaml.Diagram.Layout.DirectedTreeLayout
                {
                    Type = Syncfusion.UI.Xaml.Diagram.Layout.LayoutType.Hierarchical,
                    Orientation = Syncfusion.UI.Xaml.Diagram.Layout.TreeOrientation.LeftToRight,
                    HorizontalSpacing = 70,
                    VerticalSpacing = 22
                },
                RefreshFrequency = Syncfusion.UI.Xaml.Diagram.RefreshFrequency.Reset
            };

            if (_diagram.Info is IGraphInfo graphInfo)
                graphInfo.ItemTappedEvent += OnDiagramItemTapped;
            AssociatedObject.Children.Add(_diagram);
            RenderGraph();
        }

        /// <summary>Reset view: recreate the diagram control (discards zoom/pan) and re-fit.</summary>
        private void ResetGraphView()
        {
            try
            {
                AssociatedObject.Children.Remove(_diagram);
                BuildDiagram();
            }
            catch (Exception ex) { BehaviorLog.Write("Reset view", ex); }
        }

        private void HideSelectorChrome()
        {
            try
            {
                var selected = _diagram?.SelectedItems;
                if (selected == null) return;
                var prop = selected.GetType().GetProperty("SelectorConstraints",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                  | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(selected, Enum.ToObject(prop.PropertyType, 0));
            }
            catch (Exception ex) { BehaviorLog.Write("HideSelectorChrome", ex); }
        }

        private void RenderGraph()
        {
            if (_diagram == null || _vm?.Graph == null) return;

            var model = _vm.Graph;
            _nodeLookup.Clear();

            if (model.IsEmpty)
            {
                _diagram.Nodes = new ObservableCollection<object>();
                _diagram.Connectors = new ObservableCollection<object>();
                return;
            }

            var nodes = new ObservableCollection<object>();
            var connectors = new ObservableCollection<object>();
            var constraints = Syncfusion.UI.Xaml.Diagram.NodeConstraints.Default
                            & ~Syncfusion.UI.Xaml.Diagram.NodeConstraints.Draggable;

            foreach (var n in model.Nodes)
            {
                _nodeLookup[n.Id] = n;
                bool device = n.Kind == "Device";
                nodes.Add(new NodeViewModel
                {
                    ID = n.Id,
                    Key = n.Id,
                    UnitWidth = 195,
                    UnitHeight = 46,
                    Constraints = constraints,
                    Content = device ? $"DEVICE  -  {n.Name}" : $"{n.Name}  -  {n.RecordCount} recs",
                    ContentTemplate = CreateNodeTemplate(n.Name, device ? null : n.RecordCount, device ? "#1565C0" : "#E53935")
                });
            }

            foreach (var e in model.Edges)
            {
                connectors.Add(new ConnectorViewModel
                {
                    Key = e.Contact + "|" + e.Device,
                    SourceNodeID = e.SourceId,
                    TargetNodeID = e.TargetId,
                    Annotations = new ObservableCollection<object>
                    {
                        new TextAnnotationViewModel { Content = e.Count + " recs", Offset = new Point(0, 18) }
                    }
                });
            }

            _diagram.Nodes = nodes;
            _diagram.Connectors = connectors;
            HideSelectorChrome();
            FitGraphAfterLayout();
        }

        /// <summary>Polls until DirectedTreeLayout positions nodes, then fits them into the viewport.</summary>
        private void FitGraphAfterLayout()
        {
            var diagram = _diagram;
            var tries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (s, e) =>
            {
                tries++;
                try
                {
                    if (diagram?.Info is not IGraphInfo info || !ReferenceEquals(diagram, _diagram)) { timer.Stop(); return; }
                    var nvm = (diagram.Nodes as System.Collections.IEnumerable)?.OfType<NodeViewModel>().ToList()
                              ?? new List<NodeViewModel>();
                    bool positioned = nvm.Count > 0 && nvm.All(n => n.OffsetX != 0 || n.OffsetY != 0);
                    if (positioned || tries > 40)
                    {
                        timer.Stop();
                        var bounds = ComputeContentBounds(nvm);
                        if (!bounds.IsEmpty && bounds.Width > 0)
                            info.BringIntoViewport(bounds);
                    }
                }
                catch { timer.Stop(); }
            };
            timer.Start();
        }

        private static Rect ComputeContentBounds(List<NodeViewModel> nodes)
        {
            if (nodes.Count == 0) return Rect.Empty;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in nodes)
            {
                minX = Math.Min(minX, n.OffsetX - n.UnitWidth / 2);
                minY = Math.Min(minY, n.OffsetY - n.UnitHeight / 2);
                maxX = Math.Max(maxX, n.OffsetX + n.UnitWidth / 2);
                maxY = Math.Max(maxY, n.OffsetY + n.UnitHeight / 2);
            }
            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }

        private static DataTemplate CreateNodeTemplate(string name, int? count, string fillHex)
        {
            string esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                                     .Replace("\"", "&quot;").Replace("'", "&apos;");
            string badge = count.HasValue
                ? "<Border Grid.Column='1' Background='White' Opacity='0.92' CornerRadius='4' Padding='5,1' Margin='6,0,0,0' VerticalAlignment='Center'>" +
                  $"<TextBlock Text='{count.Value:N0}' FontSize='10' FontWeight='Bold' Foreground='#B71C1C'/></Border>"
                : string.Empty;

            string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<Border Background='" + fillHex + "' CornerRadius='8' Padding='10,6' MinHeight='34'><Grid>" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
                "<TextBlock Grid.Column='0' Text='" + esc(name) + "' FontSize='12' FontWeight='SemiBold' Foreground='White' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>" +
                badge + "</Grid></Border></DataTemplate>";
            return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private void OnDiagramItemTapped(object sender, ItemTappedEventArgs args)
        {
            if (args.Item is not NodeViewModel nvm || nvm.ID == null) return;
            if (!_nodeLookup.TryGetValue(nvm.ID.ToString(), out var info)) return;
            // Deferred: the VM may rebuild the diagram.
            AssociatedObject.Dispatcher.BeginInvoke(() => _vm.OnGraphNodeTapped(info));
        }
    }
}

