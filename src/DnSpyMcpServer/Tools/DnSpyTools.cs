using DnSpyMcpServer.Services;

namespace DnSpyMcpServer.Tools;

internal static class DnSpyTools
{
    [McpTool("list_types", "List all types in a .NET assembly.")]
    public static ToolCallResult ListTypes(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly (.dll/.exe).")]
        string assemblyPath,
        [ToolParam("Optional namespace filter (exact match).")]
        string? @namespace = null,
        [ToolParam("Include nested types in output.")]
        bool includeNested = true)
    {
        var types = ctx.Analysis.GetTypes(assemblyPath, @namespace, includeNested);
        var text = types.Length == 0 ? "No types found." : string.Join(Environment.NewLine, types);
        return new ToolCallResult(text, new
        {
            assemblyPath,
            @namespace,
            includeNested,
            count = types.Length,
            types
        });
    }

    [McpTool("decompile_type", "Decompile a type from an assembly into C#.")]
    public static ToolCallResult DecompileType(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("Type full name. Supports dnlib and reflection format.")]
        string typeFullName)
    {
        var code = ctx.Analysis.DecompileType(assemblyPath, typeFullName);
        return new ToolCallResult(code, new
        {
            assemblyPath,
            typeFullName,
            language = "csharp",
            code
        });
    }

    [McpTool("decompile_method", "Decompile one method into C#. Supports overload selection.")]
    public static ToolCallResult DecompileMethod(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("Type full name.")]
        string typeFullName,
        [ToolParam("Method name.")]
        string methodName,
        [ToolParam("Optional parameter type list to pick a specific overload (e.g. [\"System.String\",\"System.Int32\"]).")]
        string[]? parameterTypeNames = null)
    {
        var code = ctx.Analysis.DecompileMethod(assemblyPath, typeFullName, methodName, parameterTypeNames);
        return new ToolCallResult(code, new
        {
            assemblyPath,
            typeFullName,
            methodName,
            parameterTypeNames,
            language = "csharp",
            code
        });
    }

    [McpTool("get_method_il", "Get raw IL for one method. Supports overload selection.")]
    public static ToolCallResult GetMethodIl(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("Type full name.")]
        string typeFullName,
        [ToolParam("Method name.")]
        string methodName,
        [ToolParam("Optional parameter type list to pick a specific overload.")]
        string[]? parameterTypeNames = null)
    {
        var il = ctx.Analysis.GetMethodIl(assemblyPath, typeFullName, methodName, parameterTypeNames);
        return new ToolCallResult(il, new
        {
            assemblyPath,
            typeFullName,
            methodName,
            parameterTypeNames,
            language = "il",
            il
        });
    }

    [McpTool("search_members", "Search matching type/member names inside an assembly.")]
    public static ToolCallResult SearchMembers(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("Case-insensitive search text.")]
        string query,
        [ToolParam("Max number of results (default 500).")]
        int maxResults = 500)
    {
        var text = ctx.Analysis.SearchMembers(assemblyPath, query, maxResults);
        var lines = text == "No matches found." ? Array.Empty<string>() : text.Split(Environment.NewLine);
        return new ToolCallResult(text, new
        {
            assemblyPath,
            query,
            maxResults,
            count = lines.Length,
            results = lines
        });
    }

    [McpTool("list_methods", "List methods for a given type with signatures (helps overload targeting).")]
    public static ToolCallResult ListMethods(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("Type full name.")]
        string typeFullName)
    {
        var text = ctx.Analysis.ListMethods(assemblyPath, typeFullName);
        var lines = text == "No methods found." ? Array.Empty<string>() : text.Split(Environment.NewLine);
        return new ToolCallResult(text, new
        {
            assemblyPath,
            typeFullName,
            count = lines.Length,
            methods = lines
        });
    }

    [McpTool("find_string_references", "Find methods that reference a given string literal in IL (ldstr).")]
    public static ToolCallResult FindStringReferences(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.")]
        string assemblyPath,
        [ToolParam("String text to search for (path fragment, filename, token, etc.).")]
        string text,
        [ToolParam("Case-sensitive search. Default: false.")]
        bool caseSensitive = false,
        [ToolParam("Max number of matches to return. Default: 500.")]
        int maxResults = 500)
    {
        var output = ctx.Analysis.FindStringReferences(assemblyPath, text, caseSensitive, maxResults);
        var lines = output == "No string references found." ? Array.Empty<string>() : output.Split(Environment.NewLine);
        return new ToolCallResult(output, new
        {
            assemblyPath,
            text,
            caseSensitive,
            maxResults,
            count = lines.Length,
            matches = lines
        });
    }
}
