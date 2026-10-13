using ForensicViewer.Models;

namespace ForensicViewer.ViewModels
{
    /// <summary>
    /// Phase 2 — builds a contact/device relationship graph (FR-05) from the
    /// currently filtered evidence records: nodes are entities (contacts, devices),
    /// connectors aggregate their communications. Also supports FR-06 correlation
    /// lookup (all records linked to a given contact).
    /// </summary>
    public class RelationshipGraphBuilder
    {
        private readonly MainViewModel _main;

        public RelationshipGraphBuilder(MainViewModel main) => _main = main;

        /// <summary>All records related to the given contact name (FR-06).</summary>
        public List<EvidenceRecord> RecordsFor(string contactName)
        {
            if (string.IsNullOrEmpty(contactName)) return new List<EvidenceRecord>();
            return _main.FilteredRecords
                .Where(r => r.Counterparty != null &&
                            r.Counterparty.IndexOf(contactName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>Distinct entity list (contacts) present in the current filtered set, with their record counts.</summary>
        public List<(string Name, int Count)> ContactEntities()
        {
            return _main.FilteredRecords
                .Where(r => !string.IsNullOrEmpty(r.Counterparty))
                .GroupBy(r => FirstContactWord(r.Counterparty))
                .OrderByDescending(g => g.Count())
                .Select(g => (g.Key, g.Count()))
                .ToList();
        }

        private static string FirstContactWord(string counterparty) =>
            counterparty.Split('(')[0].Trim().TrimEnd();

        /// <summary>
        /// Top correlated pairs: (deviceName, contactName) with shared record counts.
        /// Drives both the diagram connectors and the grid filtering.
        /// </summary>
        public List<RelationEdge> BuildEdges(int maxEdges)
        {
            var edges = _main.FilteredRecords
                .Where(r => !string.IsNullOrEmpty(r.Counterparty))
                .GroupBy(r => new { Device = r.DeviceName, Contact = FirstContactWord(r.Counterparty) })
                .Select(g => new RelationEdge
                {
                    From = "DEVICE: " + g.Key.Device,
                    To = "CONTACT: " + g.Key.Contact,
                    Label = $"{g.Count()} records",
                    ContactName = g.Key.Contact,
                    DeviceName = g.Key.Device
                })
                .OrderByDescending(e => int.Parse(e.Label.Split(' ')[0]))
                .Take(maxEdges)
                .ToList();
            return edges;
        }

        /// <summary>Node view-model for SfDiagram data binding.</summary>
        public class GraphNode
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string UnitWidth { get; set; } = "130";
            public string UnitHeight { get; set; } = "40";
            public double OffsetX { get; set; }
            public double OffsetY { get; set; }
            public string ShapeStyle_fill { get; set; }
        }

        public class RelationEdge
        {
            public string From { get; set; }
            public string To { get; set; }
            public string Label { get; set; }
            public string ContactName { get; set; }
            public string DeviceName { get; set; }
        }
    }
}