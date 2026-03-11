namespace DnSpyMcpServer.Tools;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class McpToolAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ToolParamAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
