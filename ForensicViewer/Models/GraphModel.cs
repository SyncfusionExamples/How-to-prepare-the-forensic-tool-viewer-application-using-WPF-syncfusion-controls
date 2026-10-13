namespace ForensicViewer.Models
{
    /// <summary>UI-neutral graph node (device or contact). The view maps it to an SfDiagram node.</summary>
    public sealed class GraphNodeInfo
    {
        public string Id { get; init; }
        public string Kind { get; init; }          // "Device" | "Contact"
        public string Name { get; init; }
        public int RecordCount { get; init; }
    }

    /// <summary>UI-neutral graph edge: one (device, contact) pair with its record count.</summary>
    public sealed class GraphEdgeInfo
    {
        public string SourceId { get; init; }      // device node id
        public string TargetId { get; init; }      // contact node id
        public string Contact { get; init; }
        public string Device { get; init; }
        public int Count { get; init; }
    }

    /// <summary>Relationship graph data computed by the ViewModel from the filtered records.</summary>
    public sealed class GraphModel
    {
        public List<GraphNodeInfo> Nodes { get; } = new();
        public List<GraphEdgeInfo> Edges { get; } = new();
        public bool IsEmpty => Nodes.Count == 0;
    }

    /// <summary>One line in the Evidence Details panel.</summary>
    public sealed class DetailItem
    {
        public string Text { get; init; }
        public bool IsHeader { get; init; }
    }

    /// <summary>A chart image captured by the view for the PDF report.</summary>
    public sealed record ChartSnapshot(string Title, byte[] Png);
}
