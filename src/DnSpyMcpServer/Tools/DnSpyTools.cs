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
        try
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
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new
            {
                error = true,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
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
        try
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
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new
            {
                error = true,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
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

    [McpTool("patch_replace_string_literal", "Patch one IL string literal at a method token + IL offset. Always creates a backup first.")]
    public static ToolCallResult PatchReplaceStringLiteral(
        ToolContext ctx,
        [ToolParam("Path to target assembly (.exe/.dll).")]
        string assemblyPath,
        [ToolParam("MethodDef token (example: 0x060005C1).")]
        string methodDefToken,
        [ToolParam("IL offset (example: IL_01D2).")]
        string ilOffset,
        [ToolParam("New literal text.")]
        string newText,
        [ToolParam("Patch in place (default false).")]
        bool inPlace = false,
        [ToolParam("Output path when not inPlace. Optional.")]
        string? outputPath = null)
    {
        var result = ctx.Analysis.PatchReplaceStringLiteral(assemblyPath, methodDefToken, ilOffset, newText, inPlace, outputPath);
        return new ToolCallResult(result, new
        {
            assemblyPath,
            methodDefToken,
            ilOffset,
            newText,
            inPlace,
            outputPath,
            backupAlwaysCreated = true,
            result
        });
    }

    [McpTool("patch_nop_instructions", "Patch one or more IL instructions to NOP starting at an IL offset. Always creates a backup first.")]
    public static ToolCallResult PatchNopInstructions(
        ToolContext ctx,
        [ToolParam("Path to target assembly (.exe/.dll).")]
        string assemblyPath,
        [ToolParam("MethodDef token (example: 0x060005C1).")]
        string methodDefToken,
        [ToolParam("Start IL offset (example: IL_01DD).")]
        string ilOffset,
        [ToolParam("Number of instructions to NOP.")]
        int count = 1,
        [ToolParam("Patch in place (default false).")]
        bool inPlace = false,
        [ToolParam("Output path when not inPlace. Optional.")]
        string? outputPath = null)
    {
        var result = ctx.Analysis.PatchNopInstructions(assemblyPath, methodDefToken, ilOffset, count, inPlace, outputPath);
        return new ToolCallResult(result, new
        {
            assemblyPath,
            methodDefToken,
            ilOffset,
            count,
            inPlace,
            outputPath,
            backupAlwaysCreated = true,
            result
        });
    }

    [McpTool("format_dnspy_jump", "Build step-by-step dnSpy navigation instructions from metadata tokens.")]
    public static ToolCallResult FormatDnSpyJump(
        ToolContext ctx,
        [ToolParam("Path to assembly to open in dnSpy.")]
        string assemblyPath,
        [ToolParam("Optional TypeDef token (example: 0x02000058).")]
        string? typeDefToken = null,
        [ToolParam("Optional MethodDef token (example: 0x060005C1).")]
        string? methodDefToken = null,
        [ToolParam("Optional FieldDef token (example: 0x04000012).")]
        string? fieldDefToken = null,
        [ToolParam("Optional PropertyDef token (example: 0x17000001).")]
        string? propertyDefToken = null,
        [ToolParam("Optional IL offset (example: IL_01D2 or 01D2).")]
        string? ilOffset = null)
    {
        var normalizedType = NormalizeToken(typeDefToken);
        var normalizedMethod = NormalizeToken(methodDefToken);
        var normalizedField = NormalizeToken(fieldDefToken);
        var normalizedProperty = NormalizeToken(propertyDefToken);
        var normalizedIl = NormalizeIlOffset(ilOffset);

        var lines = new List<string>
        {
            "dnSpy navigation plan:",
            $"1) Open assembly: {assemblyPath}",
            "2) Use dnSpy metadata token navigation/search and jump to the token(s) below:"
        };

        if (normalizedType is not null) lines.Add($"   - TypeDef: {normalizedType}");
        if (normalizedMethod is not null) lines.Add($"   - MethodDef: {normalizedMethod}");
        if (normalizedField is not null) lines.Add($"   - FieldDef: {normalizedField}");
        if (normalizedProperty is not null) lines.Add($"   - PropertyDef: {normalizedProperty}");

        if (normalizedType is null && normalizedMethod is null && normalizedField is null && normalizedProperty is null)
            lines.Add("   - (No metadata token provided)");

        if (normalizedIl is not null)
        {
            lines.Add("3) Open the method body (IL view) and jump/scroll to offset:");
            lines.Add($"   - {normalizedIl}");
        }

        lines.Add("Tip: tokens from find_string_references/search_members/list_methods are directly reusable here.");

        var text = string.Join(Environment.NewLine, lines);
        return new ToolCallResult(text, new
        {
            assemblyPath,
            typeDefToken = normalizedType,
            methodDefToken = normalizedMethod,
            fieldDefToken = normalizedField,
            propertyDefToken = normalizedProperty,
            ilOffset = normalizedIl,
            steps = lines
        });
    }

    private static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var t = token.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            t = t[2..];

        if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Invalid token format: {token}");

        return $"0x{value:X8}";
    }

    private static string? NormalizeIlOffset(string? il)
    {
        if (string.IsNullOrWhiteSpace(il))
            return null;

        var x = il.Trim().ToUpperInvariant();
        if (x.StartsWith("IL_", StringComparison.Ordinal))
            x = x[3..];

        if (!int.TryParse(x, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"Invalid IL offset: {il}");

        return $"IL_{value:X4}";
    }
}
