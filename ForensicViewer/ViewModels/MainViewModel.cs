using ForensicViewer.Models;
using System.Collections.ObjectModel;

namespace ForensicViewer.ViewModels
{
    /// <summary>
    /// Central ViewModel: owns all evidence records and the composite filter state.
    /// All visualizations read from the same filtered set (FR-14 data accuracy).
    /// Cross-filtering (FR-09): chart selections push a drill filter here;
    /// FilterChanged notifies every view to rebind.
    /// </summary>
    public class MainViewModel
    {
        private readonly List<EvidenceRecord> _allRecords;
        private List<EvidenceRecord> _filteredRecords;
        private double _elapsedMs;

        // ---- Filter state (composite AND applied to all visualizations) ----
        public string SelectedEvidenceType { get; set; } = "All";
        public string SelectedApplication { get; set; } = "All";
        public string SelectedDevice { get; set; } = "All";
        public string FreeTextSearch { get; set; } = "";
        public DateTime? TimelineStart { get; set; }
        public DateTime? TimelineEnd { get; set; }

        /// <summary>
        /// Hour-of-day range filter (inclusive start, exclusive end) — set by the
        /// Activity Heatmap cell click; null means no hour filtering.
        /// </summary>
        public int? HourStart { get; set; }
        public int? HourEndExclusive { get; set; }

        /// <summary>Active drill filter set by chart selection (FR-09).</summary>
        public string DrillSource { get; private set; }   // EvidenceType | Application | Counterparty
        public string DrillValue { get; private set; }

        public event Action FilterChanged;

        public IReadOnlyList<EvidenceRecord> AllRecords => _allRecords;
        public List<EvidenceRecord> FilteredRecords => _filteredRecords;
        public double ElapsedMs => _elapsedMs;

        // ---- Aggregations (rebuilt from the SAME filtered set) ----
        public ObservableCollection<NameValuePair> EvidenceTypeCounts { get; } = new();
        public ObservableCollection<NameValuePair> ApplicationCounts { get; } = new();
        public ObservableCollection<NameValuePair> TopContacts { get; } = new();
        public ObservableCollection<TimeBucket> TimelineCounts { get; } = new();

        public MainViewModel() : this(50_000) { }

        public MainViewModel(int recordCount)
        {
            _allRecords = SampleDataGenerator.Generate(recordCount);
            ApplyFilters();
        }

        // ---------------- Filtering (FR-09, FR-10, FR-14) ----------------

        public void ApplyFilters()
        {
            var t0 = DateTime.UtcNow;

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
            SelectedEvidenceType = SelectedApplication = SelectedDevice = "All";
            FreeTextSearch = "";
            TimelineStart = TimelineEnd = null;
            HourStart = HourEndExclusive = null;
            DrillSource = DrillValue = null;
            ApplyFilters();
        }

        // ---------------- Aggregation ----------------

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

        // ---------------- Activity Heatmap (FR-07) ----------------

        /// <summary>Rows = days, columns = hours (24), value = record count in that hour.</summary>
        public ObservableCollection<ActivityCell> HeatMapData { get; } = new();

        private static readonly string[] HourLabels =
            Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();

        /// <summary>
        /// Flat cell triplets for SfHeatMap CellMapping: one item per (day, hour) cell —
        /// CellMapping treats EVERY item as a single cell {row-key, column-key, value}.
        /// </summary>
        private void RebuildHeatMapData()
        {
            HeatMapData.Clear();
            if (_filteredRecords.Count == 0) return;

            var byDayHour = _filteredRecords
                .GroupBy(r => new { r.Timestamp.Date, r.Timestamp.Hour })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Hour);

            foreach (var g in byDayHour)
                HeatMapData.Add(new ActivityCell
                {
                    DayLabel = g.Key.Date.ToString("MMM d"),
                    HourLabel = HourLabels[g.Key.Hour],
                    Count = g.Count(),
                    RealDay = g.Key.Date,
                    RealHour = g.Key.Hour
                });
        }
        // ---------------- Dropdown sources ----------------

        public IEnumerable<string> EvidenceTypes => new[] { "All" }.Concat(
            _allRecords.Select(r => r.EvidenceType.ToString()).Distinct().OrderBy(x => x));

        public IEnumerable<string> Applications => new[] { "All" }.Concat(
            _allRecords.Select(r => r.Application).Distinct().OrderBy(x => x));

        public IEnumerable<string> Devices => new[] { "All" }.Concat(
            _allRecords.Select(r => r.DeviceName).Distinct().OrderBy(x => x));
    }

    public class NameValuePair
    {
        public string Name { get; }
        public int Value { get; }
        public NameValuePair(string name, int value) { Name = name; Value = value; }
    }

    /// <summary>
    /// One heatmap CELL as a flat triplet — the shape SfHeatMap CellMapping expects:
    /// every item is one {row-key, column-key, value} tuple.
    ///   Row    -> DayLabel   ("May 3")
    ///   Column -> HourLabel  ("06:00")
    ///   Value  -> Count      (records in that day x hour)
    /// </summary>
    public class ActivityCell
    {
        public string DayLabel { get; set; }
        public string HourLabel { get; set; }
        public double Count { get; set; }
        public DateTime RealDay { get; set; }
        public int RealHour { get; set; }
    }

    public class TimeBucket
    {
        public DateTime Time { get; }
        public int Count { get; }
        public TimeBucket(DateTime time, int count) { Time = time; Count = count; }
    }
}
