# Forensic Tool Viewer — POC (Syncfusion WPF)

Proof of Concept for a Forensic Tool Viewer built entirely with **Syncfusion WPF controls (v35.1.39)**, demonstrating the customer's requirements using a single shared dataset across a four-tab workspace rendered with Syncfusion's `TabControlExt`.

**Rev 4 — final state:** Phase&nbsp;1 (core grid, charts, timeline, cross-filtering), Phase&nbsp;2 (relationship graph, correlation), Phase&nbsp;3 (drag-range date filtering, PDF summary export), and Rev&nbsp;4 (Activity Heatmap on the dedicated `SfHeatMap.WPF` control with List-mode legend and hour-level cell drill-down). **14 of 15 requirements delivered; licensed build.**

## Requirements Demonstrated

| Requirement | Syncfusion Control | Where in App |
|---|---|---|
| FR-01 Evidence Data Grid (sort/filter/group/columns) | `SfDataGrid` | Overview tab — drag headers to group area |
| FR-02 Evidence Timeline + drag-range brushing | `SfChart` `ColumnSeries` + `SfDateTimeRangeNavigator` | Overview tab — navigator strip above the timeline card; drag the thumbs to filter by date range |
| FR-03 Evidence Distribution (bar/doughnut) | `SfChart` | Overview tab — "Evidence by Type" (doughnut) & "by Application" (bar) |
| FR-04 Communication Analysis | `SfChart` `BarSeries` | Overview tab — "Top Contacts — Calls & Messages" |
| FR-05 Contact Relationship Graph *(Phase 2)* | `SfDiagram` + `DirectedTreeLayout` (LayoutManager) | "Relationship Graph" tab — tap a node to cross-filter |
| FR-06 Evidence Correlation *(Phase 2)* | Correlation master-detail grids | "Correlation" tab — select a record → related artifacts |
| FR-07 Activity Heatmap *(Rev 4)* | `SfHeatMap` (`Syncfusion.SfHeatMap.WPF`) + `SfHeatMapLegend` | "Activity Heatmap" tab — day × 24-hour matrix, List-mode legend, day/hour cell drill-down |
| FR-09 Cross-Visualization Interaction | Shared filter mediator (deferred dispatch, re-entrancy guarded) | Charts, timeline, navigator thumbs, diagram nodes **and heatmap cells** all drive one shared filter state |
| FR-10 Advanced Search & Filtering | Global header bar + navigator + heatmap hour drill | Header bar shared across all tabs; filters compose with AND semantics |
| FR-11 Performance | 50,000-record default dataset (changeable) | Filter time shown live in the header status chip |
| FR-12 Traceability | Details panel (source info, timestamps, MD5/SHA-256) | Overview tab — grid row selection |
| FR-13 Export & Reporting *(Phase 3)* | `XlsIO` (Excel) + Syncfusion `PDF` (summary) | "Export to Excel" & "Export PDF Summary" buttons on Overview tab; exports carry audit metadata |
| FR-14 Data Accuracy | Single ViewModel → every view reads the same filtered set | Verified: chart counts == grid counts == graph node counts == heatmap cells |
| FR-15 Phased approach | — | Phase 1–3 + Rev-4 heatmap delivered ahead of sequence |

**Remaining gap (customer decision required):** FR-08 media playback — no Syncfusion WPF media player; recommend re-scoping to image preview (`SfImageEditor`) or deferring.

## How to Run

```powershell
cd D:\ForensicViewerPOC\ForensicViewer
dotnet run
```

**Requirements:** .NET 8 SDK, Windows 10/11 x64.

## Project Structure

````
ForensicViewer/
├── ForensicViewer.csproj           # Syncfusion 35.1.39 package refs (incl. SfHeatMap.WPF)
├── App.xaml / App.xaml.cs           # App entry, Syncfusion license registration + global crash logging
├── MainWindow.xaml / .cs           # Four-syncfusion-tab workspace (TabControlExt):
│                                    #   Overview & Evidence / Relationship Graph / Correlation / Activity Heatmap
├── Models/
│   ├── EvidenceRecord.cs            # SRS Section 8 data model
│   └── SampleDataGenerator.cs      # Deterministic 50K synthetic records (seed 42)
└── ViewModels/
    ├── MainViewModel.cs             # Composite filter + aggregation engine (+ hour-of-day filter, heatmap data)
    └── RelationshipGraphBuilder.cs  # Phase-2 graph/correlation queries (FR-05/06)
````

## How the Cross-Filtering Works (FR-09)

All visualizations bind to one `MainViewModel`. Every filter (dropdowns, search box, timeline click, navigator drag, chart click, heatmap cell click) funnels into `MainViewModel.ApplyFilters()`, which:

1. Applies a composite AND predicate over the 50K records (evidence type, application, device, free text, date range, hour-of-day range, chart drill)
2. Rebuilds **all** aggregations — charts, graph nodes, correlation sets **and heatmap cells** — from the **same** filtered list, guaranteeing counts always match (FR-14)
3. Raises `FilterChanged` so grid and charts rebind together

Filter application is **deferred to the dispatcher and re-entrancy guarded** — never applied inside a selection/DP-change event (a lesson learned from the v35 API: applying filters inline inside chart/navigator events froze the app via `ObservableCollection` re-entrancy). The range navigator reads a **static** day list so filter updates can never feed back into it.

Clicking the same chart segment again toggles that filter off; "Clear All" resets everything. Typical filter→rebind on this dataset completes in well under 100 ms.

## Dataset (Synthetic)

`SampleDataGenerator` deterministically (seed 42) generates 50,000 records:

- **Evidence types:** Call (35%), Message (35%), BrowserHistory (12%), File (12%), Account (4%), Contact (2%)
- **Devices:** iPhone 14 (Suspect A), Samsung S23 (Suspect B), Dell Laptop XP-77, USB Drive KING-16GB
- **Contacts:** 15 named contacts with phone numbers
- **Timestamps:** 1 May 2026 → 10 Oct 2026
- **Traceability:** real MD5 + SHA-256 hashes, source artifact paths, recovery status

To test larger volumes (FR-11), change `recordCount: 50_000` in `MainWindow.xaml.cs` (e.g., to 1_000_000).

## License Note

`App.xaml.cs` registers a Syncfusion license key in the constructor:

```csharp
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("<your-key>");
```

With the key registered the app runs fully licensed — no trial banner in customer-facing demos. If the key is removed/invalid the app falls back to trial mode (banner shown) but still runs.

## POC Demo Script (~6 minutes)

1. **Launch** — 50,000 records load instantly; header status chip shows counts + live filter timing
2. **Grid basics (FR-01)** — sort any column; drag "Type" header into the group drop area
3. **Filter bar (FR-10)** — pick `WhatsApp` in App → grid, all charts and timeline update together (~100 ms, timing in status chip)
4. **Cross-filter (FR-09)** — click the doughnut "Call" slice → drill filter applies to grid and all charts; click again to release
5. **Timeline range drag (FR-02, Phase 3)** — drag the navigator thumbs above the timeline chart to select a multi-day window; all views filter to the range
6. **Top contacts (FR-04)** — click a contact bar → grid narrows to that contact's calls and messages
7. **Relationship Graph (FR-05, Phase 2)** — switch to the tab: auto-laid-out blue **device** nodes and red **contact** nodes (badge = total records; edges labelled with per-pair record counts). A node tap focuses the whole app on it; tap again (or **Clear focus**) to release. Scroll = zoom, drag background = pan; nodes stay locked in the layout. After zooming/panning, **Reset view** restores the initial fitted position. Only real communication counterparties (Name (phone)) are graphed — synthetic sources like Web/Device/Account are excluded on purpose.
8. **Correlation (FR-06, Phase 2)** — switch to the tab and click any row → every artifact linked to that contact appears in the lower grid with a live count header
9. **Activity Heatmap (FR-07, Rev 4)** — switch to the tab: day × 24-hour intensity matrix with List-mode legend (Quiet → Intense). Click a late-night cell → grid narrows to that **day and hour** (hint shows the records count); re-click the day to clear the drill
10. **Traceability (FR-12)** — back on Overview, click any grid row → details panel shows source info, UTC timestamp, MD5 and SHA-256 hashes
11. **Export (FR-13)** — "Export to Excel" → open .xlsx: audit metadata (case ID, active filters, record count) plus full records with hashes. Then "Export PDF Summary" → open PDF: filters line, KPI table with % share, embedded chart snapshots, page footers
12. **Accuracy (FR-14)** — after any filter or drill, every view's counts match the grid exactly (single shared filtered set)
