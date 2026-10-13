using ForensicViewer.Infrastructure;
using ForensicViewer.Models;
using ForensicViewer.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ForensicViewer.ViewModels
{
    /// <summary>
    /// ViewModel for the File & Media Analysis tab (FR: visualize images, videos,
    /// documents, file types, sizes, recovery statuses). Owns its own filter set
    /// (type / category / status) and ALSO inherits the shared device + timeline
    /// filters from MainViewModel via SyncWithMainFilters — so the whole app
    /// stays on ONE composite filter (FR-14 data accuracy).
    /// </summary>
    public class MediaAnalysisViewModel : ObservableObject
    {
        private List<FileArtifact> _allFiles = new();
        private List<FileArtifact> _filteredFiles = new();
        private readonly IDialogService _dialogs;

        public MediaAnalysisViewModel() : this(null) { }
        public MediaAnalysisViewModel(IDialogService dialogs) => _dialogs = dialogs ?? new DialogService();

        // ---- Own filter state ----
        private string _selectedFileType = "All";
        private string _selectedCategory = "All";
        private string _selectedRecoveryStatus = "All";

        public string SelectedFileType
        {
            get => _selectedFileType;
            set { if (SetProperty(ref _selectedFileType, value ?? "All")) ApplyFilters(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value ?? "All")) ApplyFilters(); }
        }

        public string SelectedRecoveryStatus
        {
            get => _selectedRecoveryStatus;
            set { if (SetProperty(ref _selectedRecoveryStatus, value ?? "All")) ApplyFilters(); }
        }

        // ---- Inherited from MainViewModel (set via SyncWithMainFilters) ----
        public string MainDevice { get; private set; } = "All";
        public DateTime? TimelineStart { get; private set; }
        public DateTime? TimelineEnd { get; private set; }

        // ---- View state ----
        private FileArtifact _selectedFile;
        public FileArtifact SelectedFile
        {
            get => _selectedFile;
            set { if (SetProperty(ref _selectedFile, value)) OnPropertyChanged(nameof(SelectedFileSummary)); }
        }

        public string SelectedFileSummary => SelectedFile == null
            ? "Select a file to preview"
            : $"{SelectedFile.FileName}  -  {SelectedFile.SizeDisplay}  -  {SelectedFile.RecoveryStatus}";

        public ObservableCollection<FileArtifact> FilteredFiles { get; } = new();
        public int FilteredCount => FilteredFiles.Count;

        // ---- Aggregations (rebuilt from the SAME filtered set) ----
        public ObservableCollection<NameValuePair> FileTypeCounts { get; } = new();
        public ObservableCollection<FileSizePoint> FileSizeByType { get; } = new();
        public ObservableCollection<NameValuePair> RecoveryStatusCounts { get; } = new();

        // ---- Dropdown sources ----
        public IReadOnlyList<string> FileTypes { get; private set; }
        public IReadOnlyList<string> Categories { get; private set; }
        public IReadOnlyList<string> RecoveryStatuses { get; } =
            new[] { "All", nameof(RecoveryStatus.Recovered), nameof(RecoveryStatus.Deleted), nameof(RecoveryStatus.Partial) };

        // ---- Commands ----
        public ICommand ClearFiltersCommand { get; }
        public ICommand ExportFileListCommand { get; }

        /// <summary>Loads the full artifact set and builds dropdown sources.</summary>
        public void Initialize(List<FileArtifact> allFiles)
        {
            _allFiles = allFiles ?? new List<FileArtifact>();
            BuildDropdownLists();
            ApplyFilters();
        }

        private void BuildDropdownLists()
        {
            FileTypes = new[] { "All" }
                .Concat(_allFiles.Select(f => f.FileExtension).Distinct().OrderBy(x => x))
                .ToList();
            Categories = new[] { "All" }
                .Concat(_allFiles.Select(f => f.Category.ToString()).Distinct().OrderBy(x => x))
                .ToList();
            OnPropertyChanged(nameof(FileTypes));
            OnPropertyChanged(nameof(Categories));
        }

        /// <summary>
        /// Called by MainViewModel.ApplyFilters(): inherits the application-wide device
        /// + timeline filters without re-triggering MainViewModel's own pipeline
        /// (prevents filter feedback loops).
        /// </summary>
        public void SyncWithMainFilters(string device, DateTime? start, DateTime? end)
        {
            MainDevice = device ?? "All";
            TimelineStart = start;
            TimelineEnd = end;
            ApplyFilters();
        }

        /// <summary>Composite AND filter: inherited device/timeline + own type/category/status.</summary>
        public void ApplyFilters()
        {
            IEnumerable<FileArtifact> q = _allFiles;

            if (MainDevice != "All")
                q = q.Where(f => f.DeviceName == MainDevice);
            if (TimelineStart.HasValue)
                q = q.Where(f => f.CreatedDate >= TimelineStart.Value);
            if (TimelineEnd.HasValue)
                q = q.Where(f => f.CreatedDate < TimelineEnd.Value);

            if (SelectedFileType != "All")
                q = q.Where(f => f.FileExtension == SelectedFileType);
            if (SelectedCategory != "All")
                q = q.Where(f => f.Category.ToString() == SelectedCategory);
            if (SelectedRecoveryStatus != "All")
                q = q.Where(f => f.RecoveryStatus.ToString() == SelectedRecoveryStatus);

            _filteredFiles = q.OrderByDescending(f => f.CreatedDate).ToList();

            FilteredFiles.Clear();
            foreach (var f in _filteredFiles) FilteredFiles.Add(f);
            OnPropertyChanged(nameof(FilteredCount));

            RebuildAggregations();
        }

        private void RebuildAggregations()
        {
            FileTypeCounts.Clear();
            foreach (var g in _filteredFiles
                         .GroupBy(f => f.FileExtension)
                         .OrderByDescending(g => g.Count()))
                FileTypeCounts.Add(new NameValuePair(g.Key, g.Count()));

            FileSizeByType.Clear();
            foreach (var g in _filteredFiles
                         .GroupBy(f => f.Category.ToString())
                         .OrderBy(g => g.Key))
                FileSizeByType.Add(new FileSizePoint(
                    g.Key,
                    Math.Round(g.Sum(f => f.FileSizeBytes) / 1048576.0, 1)));

            RecoveryStatusCounts.Clear();
            foreach (var g in _filteredFiles
                         .GroupBy(f => f.RecoveryStatus.ToString())
                         .OrderBy(g => g.Key))
                RecoveryStatusCounts.Add(new NameValuePair(g.Key, g.Count()));
        }

        private void ClearAllFilters()
        {
            SelectedFileType = SelectedCategory = SelectedRecoveryStatus = "All";
            // Device/timeline stay inherited from the main filter — never cleared here.
        }

        private void ExportFileList()
        {
            var path = _dialogs.PromptSavePath("Excel Workbook (*.xlsx)|*.xlsx",
                $"File_Artifacts_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            if (path == null) return;
            try
            {
                ExportService.ExportFileListToExcel(path, _filteredFiles);
                _dialogs.ShowInfo("Export Complete", $"Exported {_filteredFiles.Count:N0} file artifacts to:\n{path}");
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Error", $"Export failed: {ex.Message}");
            }
        }
    }

    /// <summary>Aggregation point for the "Storage by Category (MB)" chart.</summary>
    public class FileSizePoint
    {
        public string Name { get; }
        public double Value { get; }
        public FileSizePoint(string name, double value) { Name = name; Value = value; }
    }
}
