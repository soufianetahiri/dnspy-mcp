using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using dnlib.DotNet;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace DnSpyMcpServer.Services;

internal sealed class AssemblyAnalysisService
{
    private readonly ConcurrentDictionary<string, LoadedAssembly> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetCachedAssemblyPaths() => _cache.Keys.OrderBy(x => x).ToArray();

    public string GetAssemblySummary(string assemblyPath)
    {
        var asm = GetOrLoad(assemblyPath);
        var module = asm.Module;
        var allTypes = module.GetTypes().Where(t => !t.IsGlobalModuleType).ToArray();
        var methodCount = allTypes.Sum(t => t.Methods.Count);

        var lines = new[]
        {
            $"path: {asm.Path}",
            $"module: {module.Name}",
            $"runtime: {module.RuntimeVersion}",
            $"types: {allTypes.Length}",
            $"methods: {methodCount}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public string[] GetTypes(string assemblyPath, string? namespaceFilter, bool includeNested)
    {
        var module = GetOrLoad(assemblyPath).Module;
        return module.GetTypes()
            .Where(t => !t.IsGlobalModuleType)
            .Where(t => includeNested || !t.IsNested)
            .Where(t => string.IsNullOrWhiteSpace(namespaceFilter) || string.Equals(t.Namespace, namespaceFilter, StringComparison.Ordinal))
            .Select(t => t.FullName)
            .OrderBy(t => t)
            .ToArray();
    }

    public string ListTypes(string assemblyPath, string? namespaceFilter, bool includeNested)
    {
        var types = GetTypes(assemblyPath, namespaceFilter, includeNested);
        return types.Length == 0 ? "No types found." : string.Join(Environment.NewLine, types);
    }

    public string DecompileType(string assemblyPath, string typeFullName)
    {
        var asm = GetOrLoad(assemblyPath);
        var type = FindType(asm.Module, typeFullName);
        var code = asm.Decompiler.DecompileTypeAsString(new FullTypeName(type.ReflectionFullName));

        return $"// dnSpy location: TypeDef token {FormatToken(type.MDToken.Raw)}{Environment.NewLine}{code}";
    }

    public string DecompileMethod(string assemblyPath, string typeFullName, string methodName, string[]? parameterTypeNames)
    {
        var asm = GetOrLoad(assemblyPath);
        var type = FindType(asm.Module, typeFullName);
        var method = FindMethod(type, methodName, parameterTypeNames);

        var handle = MetadataTokens.EntityHandle((int)method.MDToken.Raw);
        var code = asm.Decompiler.DecompileAsString(new[] { handle });

        return $"// dnSpy location: TypeDef {FormatToken(type.MDToken.Raw)}, MethodDef {FormatToken(method.MDToken.Raw)}{Environment.NewLine}{code}";
    }

    public string GetMethodIl(string assemblyPath, string typeFullName, string methodName, string[]? parameterTypeNames)
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        var method = FindMethod(type, methodName, parameterTypeNames);

        if (!method.HasBody || method.Body is null)
            return "Method has no IL body.";

        var sb = new StringBuilder();
        sb.AppendLine($"dnSpy location: TypeDef {FormatToken(type.MDToken.Raw)}, MethodDef {FormatToken(method.MDToken.Raw)}");
        sb.AppendLine(RenderMethodSignature(method));
        foreach (var instruction in method.Body.Instructions)
        {
            var operand = instruction.Operand is null ? string.Empty : $" {instruction.Operand}";
            sb.AppendLine($"IL_{instruction.Offset:X4}: {instruction.OpCode}{operand}");
        }

        return sb.ToString();
    }

    public string SearchMembers(string assemblyPath, string query, int maxResults)
    {
        if (maxResults <= 0)
            maxResults = 500;

        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>(capacity: Math.Min(maxResults, 1000));

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            if (ContainsIgnoreCase(type.FullName, query))
                results.Add($"type: {type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)}");

            foreach (var method in type.Methods)
            {
                if (ContainsIgnoreCase(method.Name, query)) 
                {
                    var methodLine = $"method: {RenderMethodSignature(method)} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | MethodDef={FormatToken(method.MDToken.Raw)}";
                    var rva = GetMethodAddressRva(method);
                    if (rva != null)
                        methodLine += $" | Il2CppRVA={rva}";
                    results.Add(methodLine);
                }
            }

            foreach (var field in type.Fields)
            {
                if (ContainsIgnoreCase(field.Name, query))
                {
                    var fieldLine = $"field: {field.Name} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | FieldDef={FormatToken(field.MDToken.Raw)}";
                    var offset = GetFieldOffset(field);
                    if (offset != null)
                        fieldLine += $" | Il2CppFieldOffset={offset}";
                    results.Add(fieldLine);
                }
            }

            foreach (var property in type.Properties)
            {
                if (ContainsIgnoreCase(property.Name, query))
                {
                    results.Add(
                        $"property: {property.Name} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | PropertyDef={FormatToken(property.MDToken.Raw)}");
                }
            }

            if (results.Count >= maxResults)
                break;
        }

        return results.Count == 0
            ? "No matches found."
            : string.Join(Environment.NewLine, results.Take(maxResults));
    }

    public string FindStringReferences(string assemblyPath, string text, bool caseSensitive, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Search text cannot be empty.");

        if (maxResults <= 0)
            maxResults = 500;

        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>(Math.Min(maxResults, 1000));

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body is null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not string literal)
                        continue;

                    if (!Contains(literal, text, caseSensitive))
                        continue;

                    var match =
                        $"{type.FullName}.{RenderMethodSignature(method)} | TypeDef={FormatToken(type.MDToken.Raw)} | MethodDef={FormatToken(method.MDToken.Raw)} | IL_{instruction.Offset:X4} | \"{literal}\"";
                    results.Add(match);

                    if (results.Count >= maxResults)
                        return string.Join(Environment.NewLine, results);
                }
            }
        }

        return results.Count == 0 ? "No string references found." : string.Join(Environment.NewLine, results);
    }

    public string ListMethods(string assemblyPath, string typeFullName)
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);

        var methods = type.Methods
            .Where(m => !m.IsGetter && !m.IsSetter && !m.IsAddOn && !m.IsRemoveOn)
            .Select(m => $"{RenderMethodSignature(m)} | MethodDef={FormatToken(m.MDToken.Raw)}")
            .OrderBy(m => m)
            .ToArray();

        return methods.Length == 0
            ? "No methods found."
            : string.Join(Environment.NewLine, methods);
    }

    public string PatchReplaceStringLiteral(string assemblyPath, string methodDefToken, string ilOffset, string newText,
        bool inPlace = false, string? outputPath = null)
    {
        var sourcePath = NormalizePath(assemblyPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Assembly not found: {sourcePath}");

        var destinationPath = ResolvePatchDestination(sourcePath, inPlace, outputPath);
        var backupPath = BuildBackupPath(sourcePath);
        File.Copy(sourcePath, backupPath, overwrite: false);

        var module = ModuleDefMD.Load(sourcePath);
        var method = ResolveMethodByToken(module, methodDefToken);
        if (!method.HasBody || method.Body is null)
            throw new InvalidOperationException($"Method has no IL body: {methodDefToken}");

        var offset = ParseIlOffset(ilOffset);
        var instruction = method.Body.Instructions.FirstOrDefault(i => i.Offset == offset)
            ?? throw new InvalidOperationException($"IL offset not found in method {methodDefToken}: IL_{offset:X4}");

        if (instruction.Operand is not string oldText)
            throw new InvalidOperationException($"Instruction at IL_{offset:X4} is not a string literal (ldstr).");

        instruction.Operand = newText;
        module.Write(destinationPath);

        return string.Join(Environment.NewLine,
            "Patch applied: replace string literal",
            $"source: {sourcePath}",
            $"backup: {backupPath}",
            $"output: {destinationPath}",
            $"method: {FormatToken(method.MDToken.Raw)}",
            $"offset: IL_{offset:X4}",
            $"old: \"{oldText}\"",
            $"new: \"{newText}\"");
    }

    public string PatchNopInstructions(string assemblyPath, string methodDefToken, string ilOffset, int count,
        bool inPlace = false, string? outputPath = null)
    {
        if (count <= 0)
            throw new InvalidOperationException("count must be > 0");

        var sourcePath = NormalizePath(assemblyPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Assembly not found: {sourcePath}");

        var destinationPath = ResolvePatchDestination(sourcePath, inPlace, outputPath);
        var backupPath = BuildBackupPath(sourcePath);
        File.Copy(sourcePath, backupPath, overwrite: false);

        var module = ModuleDefMD.Load(sourcePath);
        var method = ResolveMethodByToken(module, methodDefToken);
        if (!method.HasBody || method.Body is null)
            throw new InvalidOperationException($"Method has no IL body: {methodDefToken}");

        var offset = ParseIlOffset(ilOffset);
        var instructions = method.Body.Instructions;
        var startIndex = -1;
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].Offset == offset)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
            throw new InvalidOperationException($"IL offset not found in method {methodDefToken}: IL_{offset:X4}");

        var end = Math.Min(startIndex + count, instructions.Count);
        for (var i = startIndex; i < end; i++)
        {
            instructions[i].OpCode = dnlib.DotNet.Emit.OpCodes.Nop;
            instructions[i].Operand = null;
        }

        module.Write(destinationPath);

        return string.Join(Environment.NewLine,
            "Patch applied: NOP instructions",
            $"source: {sourcePath}",
            $"backup: {backupPath}",
            $"output: {destinationPath}",
            $"method: {FormatToken(method.MDToken.Raw)}",
            $"startOffset: IL_{offset:X4}",
            $"count: {end - startIndex}");
    }

    private LoadedAssembly GetOrLoad(string assemblyPath)
    {
        var normalized = NormalizePath(assemblyPath);
        if (!File.Exists(normalized))
            throw new FileNotFoundException($"Assembly not found: {normalized}");

        return _cache.GetOrAdd(normalized, static path =>
        {
            var module = ModuleDefMD.Load(path);
            var settings = new DecompilerSettings(LanguageVersion.Latest)
            {
                ThrowOnAssemblyResolveErrors = false
            };

            var decompiler = new CSharpDecompiler(path, settings);
            return new LoadedAssembly(path, module, decompiler);
        });
    }

    private static TypeDef FindType(ModuleDefMD module, string typeFullName)
    {
        var type = module.GetTypes().FirstOrDefault(t =>
            string.Equals(t.FullName, typeFullName, StringComparison.Ordinal) ||
            string.Equals(t.ReflectionFullName, typeFullName, StringComparison.Ordinal));

        return type ?? throw new InvalidOperationException($"Type not found: {typeFullName}");
    }

    private static MethodDef FindMethod(TypeDef type, string methodName, string[]? parameterTypeNames)
    {
        var candidates = type.Methods
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"Method not found: {type.FullName}.{methodName}");

        if (parameterTypeNames is { Length: > 0 })
        {
            var normalized = parameterTypeNames.Select(NormalizeTypeName).ToArray();
            var matched = candidates.Where(m => ParametersMatch(m, normalized)).ToArray();

            if (matched.Length == 1)
                return matched[0];

            if (matched.Length == 0)
                throw new InvalidOperationException(
                    $"No overload matched parameterTypeNames for {type.FullName}.{methodName}. Available: {string.Join(" | ", candidates.Select(RenderMethodSignature))}");

            throw new InvalidOperationException(
                $"Multiple overloads matched. Provide more specific parameterTypeNames. Matches: {string.Join(" | ", matched.Select(RenderMethodSignature))}");
        }

        if (candidates.Length == 1)
            return candidates[0];

        throw new InvalidOperationException(
            $"Ambiguous method name '{methodName}'. Provide parameterTypeNames. Available: {string.Join(" | ", candidates.Select(RenderMethodSignature))}");
    }

    private static bool ParametersMatch(MethodDef method, IReadOnlyList<string> normalizedParameterTypeNames)
    {
        if (method.Parameters.Count != normalizedParameterTypeNames.Count)
            return false;

        for (var i = 0; i < method.Parameters.Count; i++)
        {
            var actual = NormalizeTypeName(method.Parameters[i].Type.FullName);
            if (!string.Equals(actual, normalizedParameterTypeNames[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string RenderMethodSignature(MethodDef method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.FullName} {p.Name}"));
        return $"{method.ReturnType.FullName} {method.Name}({parameters})";
    }

    private static string FormatToken(uint raw) => $"0x{raw:X8}";

    private static MethodDef ResolveMethodByToken(ModuleDefMD module, string methodDefToken)
    {
        var token = ParseHexToken(methodDefToken);
        var provider = module.ResolveToken(token) as MethodDef;
        return provider ?? throw new InvalidOperationException($"MethodDef token not found: {FormatToken(token)}");
    }

    private static uint ParseHexToken(string tokenText)
    {
        var t = tokenText.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            t = t[2..];

        if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Invalid token format: {tokenText}");

        return value;
    }

    private static int ParseIlOffset(string ilOffset)
    {
        var t = ilOffset.Trim();
        if (t.StartsWith("IL_", StringComparison.OrdinalIgnoreCase))
            t = t[3..];

        if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"Invalid IL offset: {ilOffset}");

        return value;
    }

    private static string ResolvePatchDestination(string sourcePath, bool inPlace, string? outputPath)
    {
        if (inPlace)
            return sourcePath;

        if (!string.IsNullOrWhiteSpace(outputPath))
            return NormalizePath(outputPath);

        var dir = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        return Path.Combine(dir, $"{name}.patched{ext}");
    }

    private static string BuildBackupPath(string sourcePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var candidate = $"{sourcePath}.{timestamp}.bak";
        return candidate;
    }

    private static string NormalizeTypeName(string typeName)
        => typeName.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace("+", ".", StringComparison.Ordinal);

    private static bool ContainsIgnoreCase(string source, string value)
        => source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string source, string value, bool caseSensitive)
        => source.Contains(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string input)
    {
        if (Path.IsPathRooted(input))
            return Path.GetFullPath(input);

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, input));
    }

    private static string? GetMethodAddressRva(MethodDef method) =>
        GetIl2CppMetadataAttributeValue(method.CustomAttributes, "AddressAttribute", "RVA");

    private static string? GetFieldOffset(FieldDef field) =>
        GetIl2CppMetadataAttributeValue(field.CustomAttributes, "FieldOffset", "Offset");

    private static string? GetIl2CppMetadataAttributeValue(IEnumerable<CustomAttribute> attributes, string attributeName, string argumentName)
    {
        foreach (var attr in attributes)
        {
            if (!attr.TypeFullName.Contains(attributeName))
                continue;
            
            var arg = attr.NamedArguments.FirstOrDefault(a => a.Name == argumentName);
            if (arg == null)
                continue;

            var value = arg.Argument.Value;
            return value switch
            {
                string s => s,
                uint u => $"0x{u:X}",
                ulong ul => $"0x{ul:X}",
                _ => value?.ToString()
            };
        }
        return null;
    }

    private sealed record LoadedAssembly(string Path, ModuleDefMD Module, CSharpDecompiler Decompiler);
}
