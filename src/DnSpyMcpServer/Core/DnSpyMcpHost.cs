using DnSpyMcpServer.Services;
using DnSpyMcpServer.Tools;
using DnSpyMcpServer.Transport;

namespace DnSpyMcpServer.Core;

internal static class DnSpyMcpHost
{
    public static McpServer CreateDefault()
    {
        var rpc = new StdioJsonRpc();
        var analysis = new AssemblyAnalysisService();
        var context = new ToolContext(analysis);
        var registry = ToolRegistry.From(typeof(DnSpyTools));

        return new McpServer(rpc, registry, context);
    }
}
