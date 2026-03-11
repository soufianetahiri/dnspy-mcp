using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DnSpyMcpServer.Tools;

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolBinding> _tools;

    private ToolRegistry(Dictionary<string, ToolBinding> tools)
    {
        _tools = tools;
    }

    public static ToolRegistry From(Type type)
    {
        var tools = new Dictionary<string, ToolBinding>(StringComparer.OrdinalIgnoreCase);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpToolAttribute>() is not null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<McpToolAttribute>()!;
            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(ToolContext))
                throw new InvalidOperationException($"Tool '{attr.Name}' must receive ToolContext as first parameter.");

            var exposedParams = parameters.Skip(1).ToArray();
            var definition = BuildDefinition(attr, exposedParams);

            tools[attr.Name] = new ToolBinding(attr.Name, definition, method, exposedParams);
        }

        return new ToolRegistry(tools);
    }

    public object[] GetDefinitions() => _tools.Values.Select(t => t.Definition).Cast<object>().ToArray();

    public ToolCallResult Invoke(string toolName, JsonElement arguments, ToolContext context)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            throw new InvalidOperationException($"Unknown tool: {toolName}");

        var invokeArgs = new object?[tool.ExposedParameters.Length + 1];
        invokeArgs[0] = context;

        for (var i = 0; i < tool.ExposedParameters.Length; i++)
        {
            var param = tool.ExposedParameters[i];
            if (TryReadArgument(arguments, param.Name!, out var value))
            {
                invokeArgs[i + 1] = ConvertJson(value, param.ParameterType, param.Name!);
            }
            else if (param.HasDefaultValue)
            {
                invokeArgs[i + 1] = param.DefaultValue;
            }
            else if (IsNullable(param.ParameterType))
            {
                invokeArgs[i + 1] = null;
            }
            else
            {
                throw new InvalidOperationException($"Missing required argument: {param.Name}");
            }
        }

        var result = tool.Method.Invoke(null, invokeArgs);
        return result switch
        {
            ToolCallResult typed => typed,
            string text => ToolCallResult.FromText(text),
            _ => ToolCallResult.FromText(result?.ToString() ?? string.Empty)
        };
    }

    private static bool TryReadArgument(JsonElement args, string name, out JsonElement value)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value))
            return true;

        value = default;
        return false;
    }

    private static object BuildDefinition(McpToolAttribute attr, ParameterInfo[] parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var parameter in parameters)
        {
            var parameterType = UnwrapNullable(parameter.ParameterType);
            var schemaType = ToJsonSchemaType(parameterType);
            var parameterDescription = parameter.GetCustomAttribute<ToolParamAttribute>()?.Description
                                     ?? $"Parameter '{parameter.Name}'";

            if (parameterType == typeof(string[]))
            {
                properties[parameter.Name!] = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = parameterDescription
                };
            }
            else
            {
                properties[parameter.Name!] = new
                {
                    type = schemaType,
                    description = parameterDescription
                };
            }

            if (!parameter.HasDefaultValue && !IsNullable(parameter.ParameterType))
                required.Add(parameter.Name!);
        }

        return new
        {
            name = attr.Name,
            description = attr.Description,
            inputSchema = new
            {
                title = attr.Name,
                description = attr.Description,
                type = "object",
                properties,
                required = required.ToArray()
            }
        };
    }

    private static object? ConvertJson(JsonElement json, Type targetType, string paramName)
    {
        var unwrapped = UnwrapNullable(targetType);

        try
        {
            if (unwrapped == typeof(string))
                return json.ValueKind == JsonValueKind.Null ? null : json.GetString();

            if (unwrapped == typeof(bool))
                return json.GetBoolean();

            if (unwrapped == typeof(int))
                return json.GetInt32();

            if (unwrapped == typeof(long))
                return json.GetInt64();

            if (unwrapped == typeof(double))
                return json.GetDouble();

            if (unwrapped == typeof(string[]))
            {
                if (json.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException($"Parameter '{paramName}' must be an array.");

                return json.EnumerateArray()
                    .Select(v => v.GetString() ?? string.Empty)
                    .ToArray();
            }

            if (unwrapped.IsEnum)
            {
                var text = json.GetString() ?? throw new InvalidOperationException();
                return Enum.Parse(unwrapped, text, ignoreCase: true);
            }

            return Convert.ChangeType(json.ToString(), unwrapped, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid argument '{paramName}': {ex.Message}");
        }
    }

    private static string ToJsonSchemaType(Type type)
    {
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) || type.IsEnum) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type == typeof(string[])) return "array";
        return "string";
    }

    private static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) is not null || !type.IsValueType;

    private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private sealed record ToolBinding(string Name, object Definition, MethodInfo Method, ParameterInfo[] ExposedParameters);
}
