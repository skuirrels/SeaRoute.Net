using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using SeaRoute.Common;
using SeaRoute.Graph;
using SeaRoute.Ports;

namespace SeaRoute.Data;

/// <summary>
/// Helper to load and deserialize embedded compressed datasets into in-memory data structures.
/// </summary>
public static class EmbeddedResources
{
    private static readonly Assembly CurrentAssembly = typeof(EmbeddedResources).Assembly;

    /// <summary>
    /// Loads and builds the MaritimeGraph from the embedded marnet.json.gz dataset.
    /// </summary>
    public static MaritimeGraph LoadMaritimeGraph()
    {
        var graph = new MaritimeGraph();

        using var rawStream = CurrentAssembly.GetManifestResourceStream("SeaRoute.Data.marnet.json.gz")
            ?? throw new InvalidOperationException("Embedded resource 'SeaRoute.Data.marnet.json.gz' not found.");

        using var gzipStream = new GZipStream(rawStream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzipStream);

        var root = doc.RootElement;
        if (root.TryGetProperty("nodes", out var nodesElement) && nodesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nodeItem in nodesElement.EnumerateArray())
            {
                double lon = nodeItem[0].GetDouble();
                double lat = nodeItem[1].GetDouble();
                graph.AddNode(new Coordinate(lon, lat));
            }
        }

        if (root.TryGetProperty("edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var edgeItem in edgesElement.EnumerateArray())
            {
                int u = edgeItem[0].GetInt32();
                int v = edgeItem[1].GetInt32();
                double weight = edgeItem[2].GetDouble();
                string? passage = edgeItem[3].ValueKind == JsonValueKind.String ? edgeItem[3].GetString() : null;

                graph.AddDirectedEdge(u, v, weight, passage);
            }
        }

        graph.BuildIndex();
        return graph;
    }

    /// <summary>
    /// Loads and builds the PortDatabase from the embedded ports.json.gz dataset.
    /// </summary>
    public static PortDatabase LoadPortDatabase()
    {
        var ports = new List<Port>();

        using var rawStream = CurrentAssembly.GetManifestResourceStream("SeaRoute.Data.ports.json.gz")
            ?? throw new InvalidOperationException("Embedded resource 'SeaRoute.Data.ports.json.gz' not found.");

        using var gzipStream = new GZipStream(rawStream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzipStream);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            return new PortDatabase(ports);

        foreach (var portElem in root.EnumerateArray())
        {
            string portCode = portElem.TryGetProperty("port", out var p) ? p.GetString() ?? "" : "";
            string name = portElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string cty = portElem.TryGetProperty("cty", out var c) ? c.GetString() ?? "" : "";
            double t = portElem.TryGetProperty("t", out var tv) ? tv.GetDouble() : 0.0;
            double x = portElem.TryGetProperty("x", out var xv) ? xv.GetDouble() : 0.0;
            double y = portElem.TryGetProperty("y", out var yv) ? yv.GetDouble() : 0.0;

            var toCountries = new List<string>();
            if (portElem.TryGetProperty("to_cty", out var toArr) && toArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toArr.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) toCountries.Add(s);
                }
            }

            ports.Add(new Port
            {
                PortCode = portCode,
                Name = name,
                Country = cty,
                TerminalFlag = t,
                ToCountries = toCountries,
                Coordinate = new Coordinate(x, y)
            });
        }

        return new PortDatabase(ports);
    }
}
