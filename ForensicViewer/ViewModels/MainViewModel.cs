using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ForensicViewer.Infrastructure;
using ForensicViewer.Models;
using ForensicViewer.Services;

namespace ForensicViewer.ViewModels
{
    /// <summary>
    /// Central ViewModel (MVVM): owns all evidence records, the composite filter state,
    /// every aggregation, commands and view state (status, details, correlation, graph
    /// model, hints). The View binds to it and contains no business logic.
    /// All visualizations read from the same filtered set (FR-14 data accuracy).
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public const int GraphTabIndex = 1;

        private readonly List<EvidenceRecord> _allRecords;
        private readonly IDialogService _dialogs;
        private readonly RelationshipGraphBuilder _graphBuilder;
        private List<EvidenceRecord> _filteredRecords = new();
        private double _elapsedMs;
        private bool _suppressApply;

        // ---- Filter state (composite AND applied to all visualizations) ----
        private string _selectedEvidenceType = "All";
        private string _selectedApplication = "All";
        private string _selectedDevice = "All";
        private string _freeTextSearch = "";

        public string SelectedEvidenceType
        {
            get => _selectedEvidenceType;
            set { if (SetProperty(ref _selectedEvidenceType, value ?? "All")) ApplyIfEnabled(); }
        }

        public string SelectedApplication
        {
            get => _selectedApplication;
            set { if (SetProperty(ref _selectedApplication, value ?? "All")) ApplyIfEnabled(); }
        }

        public string SelectedDevice
        {
            get => _selectedDevice;
            set { if (SetProperty(ref _selectedDevice, value ?? "All")) ApplyIfEnabled(); }
        }

        public string FreeTextSearch
        {
            get => _freeTextSearch;
            set { if (SetProperty(ref _freeTextSearch, value ?? "")) ApplyIfEnabled(); }
        }

        public DateTime? TimelineStart { get; set; }
        public DateTime? TimelineEnd { get; set; }

        /// <summary>Hour-of-day range (inclusive start, exclusive end) set by the heatmap cell click.</summary>
        public int? HourStart { get; set; }
        public int? HourEndExclusive { get; set; }

        /// <summary>Active drill filter set by chart selection (FR-09).</summary>
        public string DrillSource { get; private set; }   // EvidenceType | Application | Counterparty
        public string DrillValue { get; private set; }

        public event Action FilterChanged;
        /// <summary>
        /// Raised by chart behaviors BEFORE RebuildAggregates() mutates the bound collections.
        /// Accumulation series (doughnut) crash with ArgumentOutOfRangeException on the next
        /// selection if their stale SelectedIndex outlives the segment list — behaviors must
        /// clear selection here, with the old segments still valid.
        /// </summary>
        public event Action BeforeFiltersApplied;
        /// <summary>Raised by ResetGraphViewCommand; the View resets its diagram viewport.</summary>
        public event Action ResetGraphViewRequested;

        public IReadOnlyList<EvidenceRecord> AllRecords => _allRecords;
        public List<EvidenceRecord> FilteredRecords => _filteredRecords;
        public double ElapsedMs => _elapsedMs;

        // ---- Aggregations (rebuilt from the SAME filtered set) ----
        public ObservableCollection<NameValuePair> EvidenceTypeCounts { get; } = new();
        public ObservableCollection<NameValuePair> ApplicationCounts { get; } = new();
        public ObservableCollection<NameValuePair> TopContacts { get; } = new();
        public ObservableCollection<TimeBucket> TimelineCounts { get; } = new();

        /// <summary>Rows = days, columns = hours (24), value = record count in that hour.</summary>
        public ObservableCollection<ActivityCell> HeatMapData { get; } = new();

        // ---- Dropdown sources ----
        public IReadOnlyList<string> EvidenceTypes { get; }
        public IReadOnlyList<string> Applications { get; }
        public IReadOnlyList<string> Devices { get; }

        // ---- View state ----
        private string _statusText = "";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (!SetProperty(ref _selectedTabIndex, value)) return;
                if (value == GraphTabIndex) RebuildGraphModel();   // fresh data each time it is opened
            }
        }

        private EvidenceRecord _selectedRecord;
        public EvidenceRecord SelectedRecord
        {
            get => _selectedRecord;
            set { if (SetProperty(ref _selectedRecord, value)) BuildDetails(value); }
        }

        public ObservableCollection<DetailItem> DetailItems { get; } = new();

        private EvidenceRecord _correlationSelectedRecord;
        public EvidenceRecord CorrelationSelectedRecord
        {
            get => _correlationSelectedRecord;
            set { if (SetProperty(ref _correlationSelectedRecord, value)) RefreshCorrelation(); }
        }

        private string _correlationSummary = "Select a record above - related evidence appears here";
        public string CorrelationSummary { get => _correlationSummary; private set => SetProperty(ref _correlationSummary, value); }

        private List<EvidenceRecord> _relatedRecords;
        public List<EvidenceRecord> RelatedRecords { get => _relatedRecords; private set => SetProperty(ref _relatedRecords, value); }

        private const string DefaultGraphHint =
            "Click a contact or device node to focus the whole app on it. Click it again (or Clear focus) to release.";
        private string _graphHint = DefaultGraphHint;
        public string GraphHint { get => _graphHint; private set => SetProperty(ref _graphHint, value); }

        private string _graphSelectionInfo = " none";
        public string GraphSelectionInfo { get => _graphSelectionInfo; private set => SetProperty(ref _graphSelectionInfo, value); }

        private GraphModel _graph = new();
        /// <summary>Relationship graph data for the current filters (rendered by the View).</summary>
        public GraphModel Graph { get => _graph; private set => SetProperty(ref _graph, value); }

        private string _heatMapHint = "darker cells = more records; select a cell to see its records in the grid below";
        public string HeatMapHint { get => _heatMapHint; private set => SetProperty(ref _heatMapHint, value); }

        // ---- Commands ----
        public ICommand ClearAllCommand { get; }
        public ICommand ClearDrillCommand { get; }
        public ICommand ClearGraphFocusCommand { get; }
        public ICommand ResetGraphViewCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ExportPdfCommand { get; }

        /// <summary>Supplied by the View so the PDF export can embed chart images without touching controls.</summary>
        public IChartSnapshotProvider SnapshotProvider { get; set; }

        /// <summary>File & Media Analysis sub-ViewModel (5th tab): file artifacts + inherited filters.</summary>
        public MediaAnalysisViewModel MediaAnalysis { get; }

        public MainViewModel() : this(50_000) { }

        public MainViewModel(int recordCount, IDialogService dialogs = null)
        {
            _dialogs = dialogs ?? new DialogService();
            _allRecords = SampleDataGenerator.Generate(recordCount);
            _graphBuilder = new RelationshipGraphBuilder(this);

            // File & Media Analysis: own filter state + inherited device/timeline filters.
            MediaAnalysis = new MediaAnalysisViewModel(_dialogs);
            MediaAnalysis.Initialize(SampleDataGenerator.GenerateFileArtifacts(300));

            EvidenceTypes = new[] { "All" }.Concat(_allRecords.Select(r => r.EvidenceType.ToString()).Distinct().OrderBy(x => x)).ToList();
            Applications = new[] { "All" }.Concat(_allRecords.Select(r => r.Application).Distinct().OrderBy(x => x)).ToList();
            Devices = new[] { "All" }.Concat(_allRecords.Select(r => r.DeviceName).Distinct().OrderBy(x => x)).ToList();

            ClearAllCommand = new RelayCommand(ClearAllFilters);
            ClearDrillCommand = new RelayCommand(ClearDrillFilter);
            ClearGraphFocusCommand = new RelayCommand(ClearGraphFocus);
            ResetGraphViewCommand = new RelayCommand(() => ResetGraphViewRequested?.Invoke());
            ExportExcelCommand = new RelayCommand(ExportExcel);
            ExportPdfCommand = new RelayCommand(ExportPdf);

            ApplyFilters();
        }

        // ---------------- Filtering (FR-09, FR-10, FR-14) ----------------

        private void ApplyIfEnabled()
        {
            if (!_suppressApply) ApplyFilters();
        }

        public void ApplyFilters()
        {
            var t0 = DateTime.UtcNow;

            // Before mutating bound collections: charts must drop stale selection indices now.
            try { BeforeFiltersApplied?.Invoke(); } catch { /* never block filtering on view cleanup */ }

            IEnumerable<EvidenceRecord> q = _allRecords;

            if (SelectedEvidenceType != "All")
                q = q.Where(r => r.EvidenceType.ToString() == SelectedEvidenceType);

            if (SelectedApplication != "All")
                q = q.Where(r => r.Application == SelectedApplication);

            if (SelectedDevice != "All")
                q = q.Where(r => r.DeviceName == SelectedDevice);

            if (TimelineStart.HasValue)
                q = q.Where(r => r.Timestamp >= TimelineStart.Value);
            if (TimelineEnd.HasValue)
                q = q.Where(r => r.Timestamp < TimelineEnd.Value);

            // hour-of-day filtering (heatmap cell drill / unusual-hours analysis)
            if (HourStart.HasValue)
                q = q.Where(r => r.Timestamp.Hour >= HourStart.Value);
            if (HourEndExclusive.HasValue)
                q = q.Where(r => r.Timestamp.Hour < HourEndExclusive.Value);

            if (!string.IsNullOrWhiteSpace(FreeTextSearch))
                q = q.Where(r =>
                    Contains(r.Content, FreeTextSearch) ||
                    Contains(r.Counterparty, FreeTextSearch) ||
                    Contains(r.DeviceName, FreeTextSearch));

            if (DrillValue != null && DrillSource == "EvidenceType")
                q = q.Where(r => r.EvidenceType.ToString() == DrillValue);
            else if (DrillValue != null && DrillSource == "Application")
                q = q.Where(r => r.Application == DrillValue);
            else if (DrillValue != null && DrillSource == "Counterparty")
                q = q.Where(r => Contains(r.Counterparty, DrillValue));

            _filteredRecords = q.ToList();
            _elapsedMs = (DateTime.UtcNow - t0).TotalMilliseconds;

            RebuildAggregates();

            OnPropertyChanged(nameof(FilteredRecords));
            RefreshStatus();
            RefreshCorrelation();
            MediaAnalysis.SyncWithMainFilters(SelectedDevice, TimelineStart, TimelineEnd);
            if (SelectedTabIndex == GraphTabIndex) RebuildGraphModel();

            FilterChanged?.Invoke();
        }

        private static bool Contains(string source, string term) =>
            source != null && source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Set drill filter from a chart click; re-click same value toggles off.</summary>
        public void SetDrillFilter(string source, string value)
        {
            if (DrillSource == source && DrillValue == value)
            {
                ClearDrillFilter();
                return;
            }
            DrillSource = source;
            DrillValue = value;
            ApplyFilters();
        }

        public void ClearDrillFilter()
        {
            DrillSource = null;
            DrillValue = null;
            ApplyFilters();
        }

        public void ClearAllFilters()
        {
            _suppressApply = true;
            try
            {
                SelectedEvidenceType = SelectedApplication = SelectedDevice = "All";
                FreeTextSearch = "";
            }
            finally { _suppressApply = false; }

            TimelineStart = TimelineEnd = null;
            HourStart = HourEndExclusive = null;
            DrillSource = DrillValue = null;
            ApplyFilters();
        }

        // ---------------- Timeline interactions ----------------

        /// <summary>Timeline column click: select that day, or clear when it is already selected.</summary>
        public void ToggleTimelineDay(DateTime day)
        {
            if (TimelineStart == day)
                TimelineStart = TimelineEnd = null;
            else
            {
                TimelineStart = day;
                TimelineEnd = day.AddDays(1);
            }
            ApplyFilters();
        }

        /// <summary>Range-navigator selection. Returns false (no-op) when the range is unchanged.</summary>
        public bool SetTimelineRange(DateTime? startDay, DateTime? endDay)
        {
            if (startDay == null) return false;
            var end = endDay?.AddDays(1);
            if (TimelineStart == startDay && TimelineEnd == end) return false;
            TimelineStart = startDay;
            TimelineEnd = end;
            ApplyFilters();
            return true;
        }

        /// <summary>Full-range day list used as the navigator's static source.</summary>
        public List<TimeBucket> BuildNavigatorDays()
        {
            var days = new List<TimeBucket>();
            if (_allRecords.Count == 0) return days;
            var min = _allRecords.Min(r => r.Timestamp).Date;
            var max = _allRecords.Max(r => r.Timestamp).Date;
            for (var d = min; d <= max; d = d.AddDays(1))
                days.Add(new TimeBucket(d, 0));
            return days;
        }

        // ---------------- Heatmap interactions (FR-07 / FR-09) ----------------

        /// <summary>Cross-filter to a heatmap day x hour cell; re-click the same cell clears it.</summary>
        public void ToggleHeatMapCell(ActivityCell cell)
        {
            if (cell == null) return;

            bool sameCell = TimelineStart == cell.RealDay
                         && TimelineEnd == cell.RealDay.AddDays(1)
                         && HourStart == cell.RealHour;
            if (sameCell)
            {
                HourStart = HourEndExclusive = null;
                TimelineStart = TimelineEnd = null;
                HeatMapHint = "Heatmap drill cleared - showing all filtered records";
                ApplyFilters();
                return;
            }

            TimelineStart = cell.RealDay;
            TimelineEnd = cell.RealDay.AddDays(1);
            HourStart = cell.RealHour;
            HourEndExclusive = cell.RealHour + 1;
            HeatMapHint = $"Selected {cell.RealDay:MMM d} {cell.RealHour:00}:00 - grid filtered to that day & hour ({(int)cell.Count} records)";
            ApplyFilters();
        }

        public ActivityCell FindHeatMapCell(string dayLabel, string hourLabel) =>
            HeatMapData.FirstOrDefault(a => a.DayLabel == dayLabel && a.HourLabel == hourLabel);

        // ---------------- Graph (FR-05) ----------------

        /// <summary>Node tap: focus the app on a device/contact; re-tap releases.</summary>
        public void OnGraphNodeTapped(GraphNodeInfo node)
        {
            if (node == null) return;

            if (node.Kind == "Device")
            {
                if (SelectedDevice == node.Name && DrillValue == null)
                {
                    SetDeviceAndApply("All");
                    GraphSelectionInfo = " none (released)";
                }
                else
                {
                    SetDeviceAndApply(node.Name);
                    GraphSelectionInfo = " Device: " + node.Name;
                }
            }
            else
            {
                if (DrillSource == "Counterparty" && DrillValue == node.Name)
                {
                    ClearDrillFilter();
                    GraphSelectionInfo = " none (released)";
                }
                else
                {
                    SetDrillFilter("Counterparty", node.Name);
                    GraphSelectionInfo = " Contact: " + node.Name + $" ({node.RecordCount} records)";
                }
            }
        }

        /// <summary>Clear focus: release device filter + contact drill and refresh.</summary>
        private void ClearGraphFocus()
        {
            _suppressApply = true;
            try { SelectedDevice = "All"; }
            finally { _suppressApply = false; }

            if (DrillSource == "Counterparty") ClearDrillFilter();
            else ApplyFilters();
            GraphSelectionInfo = " none";
        }

        private void SetDeviceAndApply(string device)
        {
            _suppressApply = true;
            try { SelectedDevice = device; }
            finally { _suppressApply = false; }
            ApplyFilters();
        }

        /// <summary>
        /// ONE device node per device, ONE contact node per top contact, and an edge for EVERY
        /// (contact, device) pair that co-occurs in the filtered data (label = record count).
        /// </summary>
        private void RebuildGraphModel()
        {
            var edges = _filteredRecords
                // Only real communication counterparties (form: 'Name (phone)').
                .Where(r => !string.IsNullOrEmpty(r.Counterparty) && r.Counterparty.Contains('('))
                .GroupBy(r => new { Device = r.DeviceName, Contact = r.Counterparty.Split('(')[0].Trim() })
                .Select(g => new { g.Key.Device, g.Key.Contact, Count = g.Count() })
                .OrderByDescending(e => e.Count)
                .Take(40)
                .ToList();

            var contacts = edges.Select(e => e.Contact).Distinct().OrderBy(n => n).Take(12).ToList();
            var devices = edges.Select(e => e.Device).Distinct().Take(8).ToList();

            var model = new GraphModel();
            if (contacts.Count == 0 || devices.Count == 0)
            {
                GraphHint = "No contact-device relationships match the current filters.";
                GraphSelectionInfo = " none";
                Graph = model;
                return;
            }
            GraphHint = DefaultGraphHint;

            string DevId(string d) => "dev:" + d;
            string ConId(string c) => "con:" + c;

            foreach (var d in devices)
                model.Nodes.Add(new GraphNodeInfo { Id = DevId(d), Kind = "Device", Name = d, RecordCount = 0 });

            foreach (var c in contacts)
                model.Nodes.Add(new GraphNodeInfo
                {
                    Id = ConId(c), Kind = "Contact", Name = c,
                    RecordCount = edges.Where(e => e.Contact == c).Sum(e => e.Count)
                });

            foreach (var e in edges)
            {
                if (!contacts.Contains(e.Contact) || !devices.Contains(e.Device)) continue;
                model.Edges.Add(new GraphEdgeInfo
                {
                    SourceId = DevId(e.Device), TargetId = ConId(e.Contact),
                    Contact = e.Contact, Device = e.Device, Count = e.Count
                });
            }
            Graph = model;
        }

        // ---------------- Details (FR-12) & Correlation (FR-06) ----------------

        private void BuildDetails(EvidenceRecord rec)
        {
            DetailItems.Clear();
            if (rec == null) return;

            void Header(string t) => DetailItems.Add(new DetailItem { Text = t, IsHeader = true });
            void Row(string label, string value) => DetailItems.Add(new DetailItem { Text = $"{label}:  {value}" });

            Header("Source Record");
            Row("Case ID", rec.CaseId);
            Row("Record ID", rec.RecordId.ToString());
            Row("Device", rec.DeviceName);
            Row("Application", rec.Application);
            Row("Type", rec.EvidenceType.ToString());
            Row("Direction", rec.Direction.ToString());
            Row("Counterparty", rec.Counterparty);
            Row("Timestamp (UTC)", rec.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            Row("Recovery Status", rec.RecoveryStatus.ToString());
            Row("Source Info", rec.SourceInfo);
            Header("Integrity Hashes");
            Row("MD5", rec.HashMD5);
            Row("SHA-256", rec.HashSHA256);
        }

        private void RefreshCorrelation()
        {
            var rec = _correlationSelectedRecord;
            string name = rec != null && !string.IsNullOrEmpty(rec.Counterparty)
                ? rec.Counterparty.Split('(')[0].Trim()
                : null;

            if (name != null)
            {
                var related = _graphBuilder.RecordsFor(name);
                CorrelationSummary = $"Related evidence for '{name}' - {related.Count:N0} linked artifacts (calls, messages, files, accounts)";
                RelatedRecords = related;
            }
            else
            {
                CorrelationSummary = "Select a record above - related evidence appears here";
                RelatedRecords = null;
            }
        }

        // ---------------- Export (FR-13) ----------------

        private void ExportExcel()
        {
            var path = _dialogs.PromptSavePath("Excel Workbook (*.xlsx)|*.xlsx",
                $"Evidence_Export_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            if (path == null) return;
            try
            {
                ExportService.ExportExcel(path, this);
                _dialogs.ShowInfo("Export Complete", $"Exported {FilteredRecords.Count:N0} records to:\n{path}");
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Error", $"Export failed: {ex.Message}");
            }
        }

        private void ExportPdf()
        {
            var path = _dialogs.PromptSavePath("PDF Document (*.pdf)|*.pdf",
                $"Evidence_Summary_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
            if (path == null) return;
            try
            {
                var snaps = SnapshotProvider?.CaptureSnapshots() ?? Array.Empty<ChartSnapshot>();
                ExportService.ExportPdf(path, this, snaps);
                _dialogs.ShowInfo("Export Complete", $"PDF summary exported to:\n{path}");
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Error", $"PDF export failed: {ex.Message}");
            }
        }

        // ---------------- Status ----------------

        private void RefreshStatus()
        {
            var drill = DrillValue != null ? $"  |  focus: {DrillValue}" : "";
            var timeline = TimelineStart.HasValue ? $"  |  day: {TimelineStart:yyyy-MM-dd}" : "";
            StatusText = $"{FilteredRecords.Count:N0} of {AllRecords.Count:N0} records   ({ElapsedMs:F0} ms){drill}{timeline}";
        }

        // ---------------- Aggregation ----------------

        private static readonly string[] HourLabels =
            Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();

        private void RebuildAggregates()
        {
            EvidenceTypeCounts.Clear();
            foreach (var g in _filteredRecords.GroupBy(r => r.EvidenceType.ToString())
                                               .OrderByDescending(g => g.Count()))
                EvidenceTypeCounts.Add(new NameValuePair(g.Key, g.Count()));

            ApplicationCounts.Clear();
            foreach (var g in _filteredRecords.GroupBy(r => r.Application)
                                               .OrderByDescending(g => g.Count()))
                ApplicationCounts.Add(new NameValuePair(g.Key, g.Count()));

            TopContacts.Clear();
            foreach (var g in _filteredRecords
                .Where(r => r.EvidenceType == EvidenceType.Call || r.EvidenceType == EvidenceType.Message)
                .GroupBy(r => r.Counterparty)
                .OrderByDescending(g => g.Count())
                .Take(10))
                TopContacts.Add(new NameValuePair(g.Key, g.Count()));

            TimelineCounts.Clear();
            foreach (var g in _filteredRecords.GroupBy(r => r.Timestamp.Date).OrderBy(g => g.Key))
                TimelineCounts.Add(new TimeBucket(g.Key, g.Count()));

            RebuildHeatMapData();
        }

        /// <summary>Flat cell triplets for SfHeatMap CellMapping: one item per (day, hour) cell.</summary>
        private void RebuildHeatMapData()
        {
            HeatMapData.Clear();
            if (_filteredRecords.Count == 0) return;

            var byDayHour = _filteredRecords
                .GroupBy(r => new { r.Timestamp.Date, r.Timestamp.Hour })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Hour);

            foreach (var g in byDayHour)
            {
                double count = g.Count();
                HeatMapData.Add(new ActivityCell
                {
                    DayLabel = g.Key.Date.ToString("MMM d"),
                    HourLabel = HourLabels[g.Key.Hour],
                    Count = count,
                    BandValue = ActivityCell.ToBandValue(count),
                    RealDay = g.Key.Date,
                    RealHour = g.Key.Hour
                });
            }
        }
    }

    public class NameValuePair
    {
        public string Name { get; }
        public int Value { get; }
        public NameValuePair(string name, int value) { Name = name; Value = value; }
    }

    /// <summary>
    /// One heatmap CELL as a flat triplet — the shape SfHeatMap CellMapping expects:
    ///   Row -> DayLabel ("May 3"), Column -> HourLabel ("06:00"), Value -> Count.
    /// </summary>
    public class ActivityCell
    {
        public string DayLabel { get; set; }
        public string HourLabel { get; set; }
        public double Count { get; set; }
        /// <summary>
        /// Band-representative value fed to the heat map so every cell renders EXACTLY
        /// as one of the legend colors. ColorMappingContainer interpolates between its
        /// Value nodes, so raw counts between nodes render as gradient blends that
        /// match no legend entry. Mapping this instead of Count makes each cell land
        /// precisely on a ColorMapping node.
        /// </summary>
        public double BandValue { get; set; }
        public DateTime RealDay { get; set; }
        public int RealHour { get; set; }

        /// <summary>Maps a raw count to its legend band color node.</summary>
        public static double ToBandValue(double count) =>
            count < 10  ? 0    :   // Below range (<10)  -> #E3F2FD
            count <= 50 ? 10    :  // Low (10-50)       -> #BBDEFB
            count <= 100 ? 50   :  // Light (51-100)    -> #90CAF9
            count <= 200 ? 100  :  // Moderate (101-200)-> #64B5F6
            count <= 300 ? 200  :  // Busy (201-300)    -> #42A5F5
            400;                   // Heavy (400+)      -> #1565C0
    }

    public class TimeBucket
    {
        public DateTime Time { get; }
        public int Count { get; }
        public TimeBucket(DateTime time, int count) { Time = time; Count = count; }
    }
}
