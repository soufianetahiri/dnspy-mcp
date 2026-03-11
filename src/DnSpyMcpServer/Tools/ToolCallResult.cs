namespace DnSpyMcpServer.Tools;

internal sealed record ToolCallResult(string Text, object? StructuredContent = null)
{
    public static ToolCallResult FromText(string text) => new(text);
}
