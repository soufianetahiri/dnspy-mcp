namespace DnSpyMcpServer.Services;

internal sealed class ResourceRegistry(AssemblyAnalysisService analysis)
{
    private const string AssembliesUri = "dnspy://assemblies";

    public object[] ListResources()
    {
        var list = new List<object>
        {
            new
            {
                uri = AssembliesUri,
                name = "Loaded assemblies",
                description = "Assemblies loaded in the dnSpy MCP cache.",
                mimeType = "text/plain"
            }
        };

        foreach (var path in analysis.GetCachedAssemblyPaths())
        {
            var encoded = Uri.EscapeDataString(path);
            list.Add(new
            {
                uri = $"dnspy://assembly?path={encoded}&view=summary",
                name = Path.GetFileName(path),
                description = $"Summary for {path}",
                mimeType = "text/plain"
            });

            list.Add(new
            {
                uri = $"dnspy://assembly?path={encoded}&view=types",
                name = $"{Path.GetFileName(path)} types",
                description = $"All type names for {path}",
                mimeType = "text/plain"
            });
        }

        return list.ToArray();
    }

    public string ReadResource(string uriText)
    {
        if (string.Equals(uriText, AssembliesUri, StringComparison.OrdinalIgnoreCase))
        {
            var assemblies = analysis.GetCachedAssemblyPaths();
            return assemblies.Count == 0
                ? "No assemblies loaded yet. Call any tool with assemblyPath first, then resources/list again."
                : string.Join(Environment.NewLine, assemblies);
        }

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "dnspy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported resource URI: {uriText}");

        if (!string.Equals(uri.Host, "assembly", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported dnspy resource host: {uri.Host}");

        var query = ParseQuery(uri.Query);
        query.TryGetValue("path", out var encodedPath);
        var view = query.TryGetValue("view", out var tmpView) ? tmpView : "summary";

        if (string.IsNullOrWhiteSpace(encodedPath))
            throw new InvalidOperationException("Resource URI missing 'path' query parameter.");

        var path = Uri.UnescapeDataString(encodedPath);

        return view.ToLowerInvariant() switch
        {
            "summary" => analysis.GetAssemblySummary(path),
            "types" => analysis.ListTypes(path, namespaceFilter: null, includeNested: true),
            _ => throw new InvalidOperationException($"Unsupported resource view: {view}")
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(part)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(part[..idx]);
            var value = Uri.UnescapeDataString(part[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
