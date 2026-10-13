using ForensicViewer.Models;
using ForensicViewer.ViewModels;
using Syncfusion.UI.Xaml.Charts;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.XlsIO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConnectorViewModel = Syncfusion.UI.Xaml.Diagram.ConnectorViewModel;
using IGraphInfo = Syncfusion.UI.Xaml.Diagram.IGraphInfo;
using ItemTappedEventArgs = Syncfusion.UI.Xaml.Diagram.ItemTappedEventArgs;
using NodeViewModel = Syncfusion.UI.Xaml.Diagram.NodeViewModel;
using PointF = System.Drawing.PointF;
using RectangleF = System.Drawing.RectangleF;
using SfDiagram = Syncfusion.UI.Xaml.Diagram.SfDiagram;
using TextAnnotationViewModel = Syncfusion.UI.Xaml.Diagram.TextAnnotationViewModel;

namespace ForensicViewer
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM;
        private RelationshipGraphBuilder Graph;
        private bool _suppressFilterEvents;
        private bool _initialized;
        private SfDiagram _diagram;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            VM = new MainViewModel(recordCount: 50_000);
            Graph = new RelationshipGraphBuilder(VM);
            DataContext = VM;

            PopulateFilterCombos();
            RebindAll();
            VM.FilterChanged += OnFilterChanged;
            BuildDiagram();

            // Phase 3: timeline drag-range navigator
            // Static full-range day list - the navigator's source must NEVER be the live
            // TimelineCounts collection (that created a re-entrancy feedback loop:
            // filter updates clear TimelineCounts - navigator CollectionChanged -
            // SelectedData recalc - our handler - ApplyFilters - clear again - crash).
            RangeNav.ItemsSource = BuildNavigatorDays();

            var navType = typeof(Syncfusion.UI.Xaml.Charts.SfDateTimeRangeNavigator);
            var desc = DependencyPropertyDescriptor.FromName("SelectedData", navType, navType);
            desc?.AddValueChanged(RangeNav, OnRangeNavSelectionChanged);

            RefreshStatus();
        }

        private void PopulateFilterCombos()
        {
            _suppressFilterEvents = true;
            CboType.ItemsSource = VM.EvidenceTypes.ToList();
            CboType.SelectedIndex = 0;
            CboApp.ItemsSource = VM.Applications.ToList();
            CboApp.SelectedIndex = 0;
            CboDevice.ItemsSource = VM.Devices.ToList();
            CboDevice.SelectedIndex = 0;
            _suppressFilterEvents = false;
        }

        private void RebindAll()
        {
            // Detach selection awareness while we swap sources: prevents the charts
            // from raising selection events caused by our own rebind (re-entrancy).
            _chartSelectionGuard = true;
            try
            {
                EvidenceGrid.ItemsSource = VM.FilteredRecords;
                CorrelationSourceGrid.ItemsSource = VM.FilteredRecords;
                DoughnutTypes.ItemsSource = VM.EvidenceTypeCounts;
                BarApps.ItemsSource = VM.ApplicationCounts;
                SeriesTimeline.ItemsSource = VM.TimelineCounts;
                BarContacts.ItemsSource = VM.TopContacts;
            }
            finally
            {
                _chartSelectionGuard = false;
            }

            // keep correlation view valid against the new filtered set
            RefreshCorrelationFor(EvidenceGrid.SelectedItem as EvidenceRecord);
            if (MainTabs != null && MainTabs.SelectedItem is Syncfusion.Windows.Tools.Controls.TabItemExt tab && tab.Header.ToString().Contains("Relationship"))
                RebuildGraph();
        }

        private void OnFilterChanged()
        {
            // BeginInvoke defers the rebind to the next dispatcher frame so it never
            // runs re-entrantly inside a chart selection event (freeze/root cause).
            Dispatcher.BeginInvoke(() =>
            {
                RebindAll();
                RefreshStatus();
            });
        }

        // ---------------- Tabs ----------------

        private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (VM == null) return;
            if (MainTabs.SelectedItem is Syncfusion.Windows.Tools.Controls.TabItemExt tab)
            {
                var header = tab.Header?.ToString() ?? "";
                if (header.Contains("Relationship"))
                    RebuildGraph(); // rebuild each time it is opened (fresh data)
                if (header.Contains("Heatmap"))
                {
                    ActivityHeatMap.ItemsSource = VM.HeatMapData; // refresh on open
                    EnsureHeatMapMouseHook();                    // SfHeatMap raises no click events itself
                }
            }
        }

        // ---------------- Activity Heatmap (FR-07) ----------------

        private bool _heatMouseHooked;

        /// <summary>
        /// SfHeatMap v35 does not raise SelectionChanged from mouse clicks (no mouse
        /// plumbing in the control). We therefore hit-test ourselves: on click, walk
        /// the visual tree up from the hit point to the HeatMapCell, read its
        /// RowHeader/ColumnHeader CellInfo labels, and map to our ActivityCell.
        /// </summary>
        private void EnsureHeatMapMouseHook()
        {
            if (_heatMouseHooked) return;
            _heatMouseHooked = true;
            ActivityHeatMap.PreviewMouseLeftButtonDown += HeatMap_MouseDown;
        }

        private void HeatMap_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (VM == null || _heatSelecting) return;
            try
            {
                var pos = e.GetPosition(ActivityHeatMap);
                var hit = System.Windows.Media.VisualTreeHelper.HitTest(ActivityHeatMap, pos);
                if (hit?.VisualHit == null) return;

                // Bubble from the hit visual up to an ancestor HeatMapCell (ContentControl).
                DependencyObject cur = hit.VisualHit;
                while (cur != null && cur != ActivityHeatMap)
                {
                    if (cur is Syncfusion.UI.Xaml.HeatMap.HeatMapCell cell)
                    {
                        ActivityCellClicked(cell);
                        return;
                    }
                    cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
                }
            }
            catch (Exception ex) { TryLog("Heatmap mouse hit-test", ex); }
        }

        /// <summary>Cross-filter the app to the clicked heatmap cell (FR-09).</summary>
        private void ActivityCellClicked(Syncfusion.UI.Xaml.HeatMap.HeatMapCell cell)
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                      | System.Reflection.BindingFlags.Instance;
            string rowLabel = GetCellInfoValue(cell, "RowHeader", flags);
            string colLabel = GetCellInfoValue(cell, "ColumnHeader", flags);
            TryLog("Heatmap cell clicked",
                new Exception($"RowHeader=[{rowLabel}] ColumnHeader=[{colLabel}] Content=[{cell.Content}]"));
            if (rowLabel == null || colLabel == null) return;

            var match = VM.HeatMapData.FirstOrDefault(a => a.DayLabel == rowLabel && a.HourLabel == colLabel);
            if (match == null) return;

            _heatSelecting = true;
            try
            {
                // Toggle: re-clicking the same day+hour clears the drill
                bool sameCell = VM.TimelineStart == match.RealDay
                             && VM.TimelineEnd == match.RealDay.AddDays(1)
                             && VM.HourStart == match.RealHour;
                if (sameCell)
                {
                    VM.HourStart = VM.HourEndExclusive = null;
                    VM.TimelineStart = VM.TimelineEnd = null;
                    VM.ApplyFilters();
                    HeatMapHint.Text = "Heatmap drill cleared - showing all filtered records";
                    return;
                }

                VM.TimelineStart = match.RealDay;
                VM.TimelineEnd = match.RealDay.AddDays(1);
                VM.HourStart = match.RealHour;
                VM.HourEndExclusive = match.RealHour + 1;

                HeatMapHint.Text = $"Selected {match.RealDay:MMM d} {match.RealHour:00}:00 - grid filtered to that day & hour ({(int)match.Count} records)";
                Dispatcher.BeginInvoke(() => VM.ApplyFilters());
            }
            finally
            {
                _heatSelecting = false;
            }
        }

        // ---------------- Activity Heatmap (FR-07) ----------------

        /// <summary>
        /// Cell click -> cross-filter (FR-09). With CellMapping (flat triplets), the
        /// HeatMapCell.Content IS the ActivityCell {DayLabel, HourLabel, Count}.
        /// Clicking a cell filters the grid to that day AND hour; re-clicking the
        /// same day-hour clears the drill.
        /// </summary>
        private bool _heatSelecting;

        /// <summary>Reads a HeatMapCell's Row/ColumnHeader (CellInfo) display label.</summary>
        private static string GetCellInfoValue(Syncfusion.UI.Xaml.HeatMap.HeatMapCell cell, string propertyName,
            System.Reflection.BindingFlags flags)
        {
            var cellInfo = cell.GetType().GetProperty(propertyName, flags)?.GetValue(cell);
            if (cellInfo == null) return null;
            var value = cellInfo.GetType().GetProperty("Value", flags)?.GetValue(cellInfo);
            // Fall back to Content when Value is missing (headers may hold either).
            if (value == null)
                value = cellInfo.GetType().GetProperty("Content")?.GetValue(cellInfo);
            return value?.ToString();
        }

        // ---------------- Filter bar (FR-10) ----------------

        private void TypeFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents) return;
            if (CboType.SelectedItem is string t) VM.SelectedEvidenceType = t;
            VM.ApplyFilters();
        }

        private void AppFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents) return;
            if (CboApp.SelectedItem is string a) VM.SelectedApplication = a;
            VM.ApplyFilters();
        }

        private void DeviceFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents) return;
            if (CboDevice.SelectedItem is string d) VM.SelectedDevice = d;
            VM.ApplyFilters();
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (VM == null) return;
            VM.FreeTextSearch = TxtSearch.Text;
            VM.ApplyFilters();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            VM.ClearAllFilters();
            _suppressFilterEvents = true;
            CboType.SelectedIndex = 0;
            CboApp.SelectedIndex = 0;
            CboDevice.SelectedIndex = 0;
            TxtSearch.Text = "";
            _suppressFilterEvents = false;
        }

        private void ClearDrill_Click(object sender, RoutedEventArgs e) => VM.ClearDrillFilter();

        // ---------------- Cross-filtering: charts (FR-09) ----------------

        /// <summary>
        /// Guard against re-entrancy: rebinding a chart's ItemsSource inside a
        /// selection handler can raise SelectionChanging synchronously again,
        /// which previously caused recursion (stack overflow / frozen app).
        /// </summary>
        private bool _chartSelectionGuard;

        private void Chart_SelectionChanging(object sender, ChartSelectionChangingEventArgs e)
        {
            if (_chartSelectionGuard || !e.IsDataPointSelection || e.SelectedSegment == null) return;

            var series = e.SelectedSegment.Series;
            var item = e.SelectedSegment.Item as NameValuePair;
            var bucket = e.SelectedSegment.Item as TimeBucket;

            // ---- Apply the filter AFTER the selection event fully unwinds. ----
            // Doing it inline mutates the chart's bound collections while the chart
            // is still mid-event (with ListenPropertyChange=True this re-enters the
            // chart internals and froze/crashed the app). Deferring to the dispatcher
            // puts the filter+rebind on a fresh call stack.
            if (ReferenceEquals(series, SeriesTimeline) && bucket != null)
            {
                var clickedDay = bucket.Time;
                Dispatcher.BeginInvoke(() =>
                {
                    if (VM.TimelineStart == clickedDay)
                        VM.TimelineStart = VM.TimelineEnd = null;
                    else
                    {
                        VM.TimelineStart = clickedDay;
                        VM.TimelineEnd = clickedDay.AddDays(1);
                    }
                    VM.ApplyFilters();
                });
                return;
            }

            if (item == null) return;

            string source = ReferenceEquals(series, DoughnutTypes) ? "EvidenceType"
                          : ReferenceEquals(series, BarApps) ? "Application"
                          : "Counterparty";
            var clickedName = item.Name;
            Dispatcher.BeginInvoke(() => VM.SetDrillFilter(source, clickedName));
        }

        // ---------------- Overview tab: grid -> Details (FR-12) ----------------

        private void Grid_SelectionChanged(object sender, GridSelectionChangedEventArgs e)
        {
            var rec = EvidenceGrid.SelectedItem as EvidenceRecord;
            ShowDetails(rec);
        }

        private void ShowDetails(EvidenceRecord rec)
        {
            DetailsPanel.Children.Clear();
            if (rec == null) return;

            DetailsPanel.Children.Add(Header("Source Record"));
            AddRow("Case ID", rec.CaseId);
            AddRow("Record ID", rec.RecordId.ToString());
            AddRow("Device", rec.DeviceName);
            AddRow("Application", rec.Application);
            AddRow("Type", rec.EvidenceType.ToString());
            AddRow("Direction", rec.Direction.ToString());
            AddRow("Counterparty", rec.Counterparty);
            AddRow("Timestamp (UTC)", rec.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            AddRow("Recovery Status", rec.RecoveryStatus.ToString());
            AddRow("Source Info", rec.SourceInfo);
            DetailsPanel.Children.Add(Header("Integrity Hashes"));
            AddRow("MD5", rec.HashMD5);
            AddRow("SHA-256", rec.HashSHA256);
        }

        private TextBlock Header(string text) => new()
        { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4), FontSize = 13 };

        private void AddRow(string label, string value) => DetailsPanel.Children.Add(new TextBlock
        {
            Text = $"{label}:  {value}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        });

        // ---------------- Correlation tab (FR-06) ----------------

        private void CorrelationSourceGrid_SelectionChanged(object sender, GridSelectionChangedEventArgs e)
        {
            RefreshCorrelationFor(CorrelationSourceGrid.SelectedItem as EvidenceRecord);
        }

        private void RefreshCorrelationFor(EvidenceRecord rec)
        {
            string name = rec != null && !string.IsNullOrEmpty(rec.Counterparty)
                ? rec.Counterparty.Split('(')[0].Trim()
                : null;

            if (name != null && VM != null)
            {
                var related = Graph.RecordsFor(name);
                CorrelationSummary.Text = $"Related evidence for '{name}' - {related.Count:N0} linked artifacts (calls, messages, files, accounts)";
                RelatedGrid.ItemsSource = related;
            }
            else
            {
                CorrelationSummary.Text = "Select a record above - related evidence appears here";
                RelatedGrid.ItemsSource = null;
            }
        }

        // ---------------- Relationship Graph tab (FR-05) ----------------

        /// <summary>Typed payload kept out-of-band, keyed by node ID (avoids fragile bindings).</summary>
        private sealed class GraphNodePayload
        {
            public string Kind;      // "Device" | "Contact"
            public string Name;      // entity name (device name or bare contact name)
            public int RecordCount;
        }

        /// <summary>ID -> payload for every graph node currently on screen.</summary>
        private readonly Dictionary<string, GraphNodePayload> _nodePayloads = new();

        private void BuildDiagram()
        {
            _diagram = new SfDiagram
            {
                // ZoomPan: scroll-zoom + background pan. MultipleSelect is REQUIRED for
                // node taps to raise ItemTappedEvent (verified empirically); the selector
                // chrome it normally draws ("cross" resize/rotate handles) is disabled
                // below, so clicks carry no visual border.
                Tool = Syncfusion.UI.Xaml.Diagram.Tool.ZoomPan | Syncfusion.UI.Xaml.Diagram.Tool.MultipleSelect,
                SelectorHandleDisplayMode = Syncfusion.UI.Xaml.Diagram.SelectorHandleDisplayMode.Compact,
                IsEnabled = true,
                Focusable = true
            };

            // Hide ALL selector chrome: no resizer / rotator / pivot ("cross") on click.
            // Applied defensively (the selector may only exist after load, so retry in
            // the RebuildGraph pass as well).
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
            GraphHost.Children.Add(_diagram);
            RebuildGraph();
        }

        /// <summary>
        /// Hides the diagram selector's visual chrome (resizer/rotator/pivot handles -
        /// the "cross" border drawn around a clicked node). Works against both the
        /// SelectorViewModel and the internal SelectorWrapper via reflection.
        /// </summary>
        private void HideSelectorChrome()
        {
            try
            {
                var selected = _diagram?.SelectedItems;
                if (selected == null) return;

                var scProp = selected.GetType().GetProperty("SelectorConstraints",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                  | System.Reflection.BindingFlags.Instance);
                if (scProp != null && scProp.CanWrite)
                {
                    var noneValue = Enum.ToObject(scProp.PropertyType, 0); // SelectorConstraints.None
                    scProp.SetValue(selected, noneValue);
                }
            }
            catch (Exception ex) { TryLog("HideSelectorChrome", ex); }
        }

        /// <summary>
        /// Rebuilds the graph from the currently filtered records:
        /// ONE device node per device, ONE contact node per top contact, and an edge for
        /// EVERY (contact, device) pair that actually co-occurs in the data (label = record count).
        /// </summary>
        private void RebuildGraph()
        {
            if (_diagram == null || VM == null) return;

            var edges = VM.FilteredRecords
                // Only real communication counterparties (form: 'Name (phone)').
                // Synthetic ones ('Web', 'Device', 'Account') are not persons and would
                // pollute the contact-device map with meaningless mega-nodes.
                .Where(r => !string.IsNullOrEmpty(r.Counterparty) && r.Counterparty.Contains('('))
                .GroupBy(r => new { Device = r.DeviceName, Contact = r.Counterparty.Split('(')[0].Trim() })
                .Select(g => new { g.Key.Device, g.Key.Contact, Count = g.Count() })
                .OrderByDescending(e => e.Count)
                .Take(40)                                   // readable graph, top 40 relationships
                .ToList();

            var contacts = edges.Select(e => e.Contact).Distinct().OrderBy(n => n).Take(12).ToList();
            var devices  = edges.Select(e => e.Device).Distinct().Take(8).ToList();

            _nodePayloads.Clear();

            if (contacts.Count == 0 || devices.Count == 0)
            {
                GraphHint.Text = "No contact-device relationships match the current filters.";
                GraphSelectionInfo.Text = " none";
                _diagram.Nodes = new ObservableCollection<object>();
                _diagram.Connectors = new ObservableCollection<object>();
                return;
            }
            GraphHint.Text = "Click a contact or device node to focus the whole app on it. Click it again (or Clear focus) to release.";

            var nodes = new ObservableCollection<object>();
            var connectors = new ObservableCollection<object>();

            // Nodes are selectable (required for ItemTappedEvent) but NOT draggable,
            // so the auto-layout stays intact.
            var constraints = Syncfusion.UI.Xaml.Diagram.NodeConstraints.Default
                            & ~Syncfusion.UI.Xaml.Diagram.NodeConstraints.Draggable;

            // ---- device nodes (roots, blue) ----
            string DevId(string d) => "dev:" + d;
            foreach (var d in devices)
            {
                _nodePayloads[DevId(d)] = new GraphNodePayload { Kind = "Device", Name = d, RecordCount = 0 };
                nodes.Add(new NodeViewModel
                {
                    ID = DevId(d),
                    Key = DevId(d),
                    UnitWidth = 195,
                    UnitHeight = 46,
                    Constraints = constraints,
                    // Plain string content: rendered as text even if a custom template is ignored.
                    Content = $"DEVICE  -  {d}",
                    ContentTemplate = CreateNodeTemplate(d, null, "#1565C0")
                });
            }

            // ---- contact nodes (red) ----
            string ConId(string c) => "con:" + c;
            foreach (var c in contacts)
            {
                var total = edges.Where(e => e.Contact == c).Sum(e => e.Count);
                _nodePayloads[ConId(c)] = new GraphNodePayload { Kind = "Contact", Name = c, RecordCount = total };
                nodes.Add(new NodeViewModel
                {
                    ID = ConId(c),
                    Key = ConId(c),
                    UnitWidth = 195,
                    UnitHeight = 46,
                    Constraints = constraints,
                    Content = $"{c}  -  {total} recs",
                    ContentTemplate = CreateNodeTemplate(c, total, "#E53935")
                });
            }

            // ---- REAL edges: every (contact,device) co-occurrence in the filtered data ----
            foreach (var e in edges)
            {
                if (!contacts.Contains(e.Contact) || !devices.Contains(e.Device)) continue;
                connectors.Add(new ConnectorViewModel
                {
                    Key = e.Contact + "|" + e.Device,
                    SourceNodeID = DevId(e.Device),
                    TargetNodeID = ConId(e.Contact),
                    Annotations = new ObservableCollection<object>
                    {
                        new TextAnnotationViewModel { Content = e.Count + " recs", Offset = new Point(0, 18) }
                    }
                });
            }

            _diagram.Nodes = nodes;
            _diagram.Connectors = connectors;

            // Re-assert chrome-less selection: new layout may have re-created the selector.
            HideSelectorChrome();

            // Wait a layout pass, then fit the whole graph into view ("initial position"):
            // read the DirectedTreeLayout-assigned node offsets, compute content bounds,
            // and bring those bounds into the viewport.
            FitGraphAfterLayout();
        }

        /// <summary>
        /// DirectedTreeLayout assigns node positions asynchronously after Nodes are set.
        /// Poll (dispatcher timer) until positions are non-default, then BringIntoViewport
        /// with the union of all node bounds so the FULL graph is visible from the start.
        /// </summary>
        private void FitGraphAfterLayout()
        {
            var tries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            timer.Tick += (s, e) =>
            {
                tries++;
                try
                {
                    if (_diagram?.Info is not IGraphInfo info) { timer.Stop(); return; }
                    var nvm = (_diagram.Nodes as System.Collections.IEnumerable)?.OfType<NodeViewModel>().ToList() ?? new System.Collections.Generic.List<NodeViewModel>();
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

        private static Rect ComputeContentBounds(System.Collections.Generic.List<NodeViewModel> nodes)
        {
            if (nodes.Count == 0) return Rect.Empty;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in nodes)
            {
                minX = System.Math.Min(minX, n.OffsetX - n.UnitWidth / 2);
                minY = System.Math.Min(minY, n.OffsetY - n.UnitHeight / 2);
                maxX = System.Math.Max(maxX, n.OffsetX + n.UnitWidth / 2);
                maxY = System.Math.Max(maxY, n.OffsetY + n.UnitHeight / 2);
            }
            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }

        /// <summary>
        /// Node visual: colored card with the label text baked in via literal XAML string
        /// (self-contained - no {Binding}, so it can never render blank), plus an optional
        /// white record-count badge for contacts.
        /// </summary>
        private System.Windows.DataTemplate CreateNodeTemplate(string name, int? count, string fillHex)
        {
            // XML-encode the label so quotes/ampersands cannot break the parsed XAML
            string esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                                     .Replace("\"", "&quot;").Replace("'", "&apos;");

            string title = esc(name);
            string badge = count.HasValue
                ? "<Border Grid.Column='1' Background='White' Opacity='0.92' CornerRadius='4' Padding='5,1' Margin='6,0,0,0' VerticalAlignment='Center'>" +
                  $"<TextBlock Text='{count.Value:N0}' FontSize='10' FontWeight='Bold' Foreground='#B71C1C'/>" +
                  "</Border>"
                : string.Empty;

            string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "  <Border Background='" + fillHex + "' CornerRadius='8' Padding='10,6' MinHeight='34'>" +
                "    <Grid>" +
                "      <Grid.ColumnDefinitions>" +
                "        <ColumnDefinition Width='*'/>" +
                "        <ColumnDefinition Width='Auto'/>" +
                "      </Grid.ColumnDefinitions>" +
                "      <TextBlock Grid.Column='0' Text='" + title + "' FontSize='12' FontWeight='SemiBold'" +
                "                 Foreground='White' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>" +
                badge +
                "    </Grid>" +
                "  </Border>" +
                "</DataTemplate>";

            return (System.Windows.DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        /// <summary>Node click -> focus app on that contact/device; re-click same -> release.</summary>
        private void OnDiagramItemTapped(object sender, ItemTappedEventArgs args)
        {
            if (args.Item is not NodeViewModel nvm) return;
            // Payload lookup by ID (content hosting can't break this)
            if (nvm.ID == null || !_nodePayloads.TryGetValue(nvm.ID.ToString(), out var payload)) return;

            if (payload.Kind == "Device")
            {
                // toggle: same device already focused -> release
                if (VM.SelectedDevice == payload.Name && VM.DrillValue == null)
                {
                    VM.SelectedDevice = "All";
                    GraphSelectionInfo.Text = " none (released)";
                }
                else
                {
                    VM.SelectedDevice = payload.Name;
                    GraphSelectionInfo.Text = " Device: " + payload.Name;
                }
                VM.ApplyFilters();
            }
            else
            {
                // toggle: same contact already drilled -> release
                if (VM.DrillSource == "Counterparty" && VM.DrillValue == payload.Name)
                {
                    VM.ClearDrillFilter();
                    GraphSelectionInfo.Text = " none (released)";
                }
                else
                {
                    VM.SetDrillFilter("Counterparty", payload.Name);
                    GraphSelectionInfo.Text = " Contact: " + payload.Name + $" ({payload.RecordCount} records)";
                }
            }
        }

        /// <summary>
        /// Reset view: brings the full content bounds back into the viewport - the same
        /// "initial position" the graph gets after a rebuild. No Syncfusion FitToPage
        /// command involved (it throws NRE in ArrangeOverride without PageSettings).
        /// </summary>
        private void GraphResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_diagram == null || VM == null) return;
            try
            {
                // Rebuild re-runs layout and the post-layout fit, which is the path
                // known to restore the initial position/zoom reliably.
                // Recreate the diagram control: discards all zoom/pan state, then
                // BuildDiagram re-adds it and re-runs layout + fit.
                GraphHost.Children.Remove(_diagram);
                BuildDiagram();
                GraphHint.Text = "View reset - full graph fitted back into the window.";
            }
            catch (Exception ex)
            {
                TryLog("Reset view (BringIntoViewport)", ex);
                GraphHint.Text = "View reset failed - see crash log.";
            }
        }
        /// <summary>Best-effort crash log append (never throws into the UI).</summary>
        private static void TryLog(string title, Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(
                    App.LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GRAPH: {title}\n{ex}\n\n");
            }
            catch { /* logging must never crash the app */ }
        }

        /// <summary>Clear focus: release device filter + contact drill and refresh.</summary>
        private void GraphReset_Click(object sender, RoutedEventArgs e)
        {
            VM.SelectedDevice = "All";
            if (VM.DrillSource == "Counterparty") VM.ClearDrillFilter();
            else VM.ApplyFilters();
            GraphSelectionInfo.Text = " none";
        }

        // ---------------- Timeline range navigator (Phase 3 / FR-02 drag-brush) ----------------

        /// <summary>Static full-range day buckets for the navigator (built once at load).</summary>
        /// <summary>
        /// Guard against feedback: the navigator recalculates SelectedData internally
        /// (on load, label clicks, thumb drags). We only act when the range actually
        /// differs from the applied one, and defer ApplyFilters to the dispatcher so
        /// we never mutate collections inside a DP-change callback.
        /// </summary>
        private bool _rangeNavApplying;

        private void OnRangeNavSelectionChanged(object sender, EventArgs e)
        {
            if (VM == null || _rangeNavApplying) return;

            var items = RangeNav.SelectedData as System.Collections.IEnumerable;
            if (items == null) return;

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

            // Idempotence: no-op changes (navigator's own internal recalcs that land
            // on the current range) are ignored - this breaks the feedback loop
            if (VM.TimelineStart == start && VM.TimelineEnd == end?.AddDays(1))
                return;

            _rangeNavApplying = true;
            var s = start;
            var ed = end?.AddDays(1);
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    VM.TimelineStart = s;
                    VM.TimelineEnd = ed;
                    VM.ApplyFilters();
                }
                finally
                {
                    _rangeNavApplying = false;
                }
            });
        }

        /// <summary>Builds the navigator's static full-range day list (never mutated later).</summary>
        private List<TimeBucket> BuildNavigatorDays()
        {
            var days = new List<TimeBucket>();
            if (VM == null || VM.AllRecords.Count == 0) return days;
            var min = VM.AllRecords.Min(r => r.Timestamp).Date;
            var max = VM.AllRecords.Max(r => r.Timestamp).Date;
            for (var d = min; d <= max; d = d.AddDays(1))
                days.Add(new TimeBucket(d, 0));
            return days;
        }

        // ---------------- Export (FR-13) ----------------

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"Evidence_Export_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            var records = VM.FilteredRecords;
            try
            {
                using var engine = new ExcelEngine();
                var workbook = engine.Excel.Workbooks.Create(1);
                var sheet = workbook.Worksheets[0];

                sheet.Range["A1"].Text = "Forensic Tool Viewer - Evidence Export";
                sheet.Range["A2"].Text = "Case ID: CASE-2026-001";
                sheet.Range["A3"].Text = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)";
                sheet.Range["A4"].Text = "Filters: " +
                    $"Type={VM.SelectedEvidenceType}; App={VM.SelectedApplication}; Device={VM.SelectedDevice}; " +
                    $"Search='{VM.FreeTextSearch}'; " +
                    $"Timeline={(VM.TimelineStart?.ToString("yyyy-MM-dd") ?? "all")} to {(VM.TimelineEnd?.ToString("yyyy-MM-dd") ?? "all")}; " +
                    $"ChartDrill={(VM.DrillValue ?? "none")}";
                sheet.Range["A5"].Text = $"Record count: {records.Count:N0}";
                sheet.Range["A1"].CellStyle.Font.Bold = true;

                int headerRow = 7;
                string[] headers = { "Timestamp", "Type", "Application", "Device", "Direction",
                                     "Counterparty", "Content", "Status", "SizeBytes", "MD5", "SHA-256" };
                for (int i = 0; i < headers.Length; i++)
                {
                    sheet.Range[headerRow, i + 1].Text = headers[i];
                    sheet.Range[headerRow, i + 1].CellStyle.Font.Bold = true;
                }

                int row = headerRow + 1;
                foreach (var r in records)
                {
                    sheet.Range[row, 1].DateTime = r.Timestamp;
                    sheet.Range[row, 2].Text = r.EvidenceType.ToString();
                    sheet.Range[row, 3].Text = r.Application;
                    sheet.Range[row, 4].Text = r.DeviceName;
                    sheet.Range[row, 5].Text = r.Direction.ToString();
                    sheet.Range[row, 6].Text = r.Counterparty;
                    sheet.Range[row, 7].Text = r.Content;
                    sheet.Range[row, 8].Text = r.RecoveryStatus.ToString();
                    sheet.Range[row, 9].Number = r.SizeBytes;
                    sheet.Range[row, 10].Text = r.HashMD5;
                    sheet.Range[row, 11].Text = r.HashSHA256;
                    row++;
                }

                sheet.UsedRange.AutofitColumns();
                workbook.SaveAs(dlg.FileName);

                MessageBox.Show($"Exported {records.Count:N0} records to:\n{dlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------- Export PDF summary (Phase 3 / FR-13) ----------------

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = $"Evidence_Summary_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var doc = new Syncfusion.Pdf.PdfDocument();
                var page = doc.Pages.Add();
                var g = page.Graphics;
                float width = page.GetClientSize().Width;
                float y = 0;

                var titleFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                    new System.Drawing.Font("Segoe UI", 20, System.Drawing.FontStyle.Bold), true);
                var hFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                    new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold), true);
                var bodyFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                    new System.Drawing.Font("Segoe UI", 10), true);
                var smallFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                    new System.Drawing.Font("Segoe UI", 8.5f), true);
                var brush = new Syncfusion.Pdf.Graphics.PdfSolidBrush(System.Drawing.Color.Black);

                g.DrawString("Forensic Tool Viewer - Evidence Summary", titleFont, brush, new PointF(0, y));
                y += 28;
                g.DrawString("CASE-2026-001", hFont, brush, new PointF(0, y));
                y += 18;
                g.DrawString($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)", bodyFont, brush, new PointF(0, y));
                y += 14;

                var fLine =
                    $"Active filters - Type: {VM.SelectedEvidenceType} | App: {VM.SelectedApplication} | " +
                    $"Device: {VM.SelectedDevice} | Search: '{VM.FreeTextSearch}' | " +
                    $"Timeline: {(VM.TimelineStart?.ToString("yyyy-MM-dd") ?? "all")} to {(VM.TimelineEnd?.ToString("yyyy-MM-dd") ?? "all")}" +
                    $" | Chart focus: {(VM.DrillValue ?? "none")}";
                g.DrawString(fLine, bodyFont, brush, new RectangleF(0, y, width, 60));
                y += 30;
                g.DrawLine(new Syncfusion.Pdf.Graphics.PdfPen(System.Drawing.Color.FromArgb(30, 136, 229), 2),
                    0, y, width, y);
                y += 12;

                // ---- KPI table: the currently displayed (filtered) counts ----
                g.DrawString("Evidence by Type (current filters)", hFont, brush, new PointF(0, y));
                y += 18;

                var grid = new Syncfusion.Pdf.Grid.PdfGrid();
                grid.Columns.Add(3);
                grid.Headers.Add(1);
                grid.Headers[0].Cells[0].Value = "Evidence Type";
                grid.Headers[0].Cells[1].Value = "Records";
                grid.Headers[0].Cells[2].Value = "% of filtered set";
                grid.Headers[0].Style.Font = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                    new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold), true);
                grid.Style.Font = smallFont;

                int total = VM.FilteredRecords.Count;
                foreach (var p in VM.EvidenceTypeCounts)
                {
                    var row = grid.Rows.Add();
                    row.Cells[0].Value = p.Name;
                    row.Cells[1].Value = p.Value.ToString("N0");
                    row.Cells[2].Value = total > 0 ? (100.0 * p.Value / total).ToString("F1") + "%" : "0%";
                }
                var layout = new Syncfusion.Pdf.Graphics.PdfLayoutFormat
                {
                    Break = Syncfusion.Pdf.Graphics.PdfLayoutBreakType.FitElement,
                    Layout = Syncfusion.Pdf.Graphics.PdfLayoutType.Paginate
                };
                var result = grid.Draw(page, new RectangleF(0, y, width, page.GetClientSize().Height - y), layout);
                y = result.Bounds.Bottom + 20;

                // ---- chart snapshots (visual summary) ----
                page = doc.Pages.Add();
                y = 0;
                g = page.Graphics;
                g.DrawString("Visual Summary - Chart Snapshots", hFont, brush, new PointF(0, y));
                y += 22;

                foreach (var kv in new (string Title, FrameworkElement Chart)[]
                         { ("Evidence by Type", ChartTypes), ("Evidence by Application", ChartApps) })
                {
                    try
                    {
                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)kv.Chart.ActualWidth, (int)kv.Chart.ActualHeight, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        kv.Chart.Measure(new Size(kv.Chart.ActualWidth, kv.Chart.ActualHeight));
                        kv.Chart.Arrange(new Rect(new Size(kv.Chart.ActualWidth, kv.Chart.ActualHeight)));
                        rtb.Render(kv.Chart);

                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                        using var ms = new System.IO.MemoryStream();
                        encoder.Save(ms);
                        var img = Syncfusion.Pdf.Graphics.PdfImage.FromStream(ms);

                        float w = Math.Min(width, (float)img.Width * 72f / 96f);
                        float h = w * img.Height / img.Width;
                        if (y + h > page.GetClientSize().Height)
                        {
                            page = doc.Pages.Add();
                            y = 0;
                            g = page.Graphics;
                        }
                        g.DrawString(kv.Title, bodyFont, brush, new PointF(0, y));
                        y += 16;
                        g.DrawImage(img, new RectangleF(0, y, w, h));
                        y += h + 14;
                    }
                    catch { /* chart not rendered yet (tab off-screen) - skip snapshot */ }
                }

                // footer
                for (int i = 0; i < doc.Pages.Count; i++)
                {
                    var p = doc.Pages[i];
                    p.Graphics.DrawString(
                        $"Forensic Tool Viewer POC - CASE-2026-001 - page {i + 1}/{doc.Pages.Count}",
                        smallFont, brush,
                        new PointF(0, p.GetClientSize().Height - 10));
                }

                doc.Save(dlg.FileName);
                MessageBox.Show($"PDF summary exported to:\n{dlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDF export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------- Status ----------------

        private void RefreshStatus()
        {
            if (VM == null) return;
            var drill = VM.DrillValue != null ? $"  |  focus: {VM.DrillValue}" : "";
            var timeline = VM.TimelineStart.HasValue ? $"  |  day: {VM.TimelineStart:yyyy-MM-dd}" : "";
            TxtStatus.Text = $"{VM.FilteredRecords.Count:N0} of {VM.AllRecords.Count:N0} records   ({VM.ElapsedMs:F0} ms){drill}{timeline}";
        }
    }
}

