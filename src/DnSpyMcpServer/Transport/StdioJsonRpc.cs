using System.Text;
using System.Text.Json;

namespace DnSpyMcpServer.Transport;

internal sealed class StdioJsonRpc
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Stream _input = Console.OpenStandardInput();
    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool? _lineFraming;

    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var firstByte = new byte[1];
        while (true)
        {
            var n = await _input.ReadAsync(firstByte.AsMemory(0, 1), cancellationToken);
            if (n == 0)
                return null;

            if (firstByte[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                continue;

            break;
        }

        if (firstByte[0] is (byte)'{' or (byte)'[')
        {
            _lineFraming = true;
            return await ReadLineFramedJsonAsync(firstByte[0], cancellationToken);
        }

        _lineFraming = false;
        var headers = await ReadHeadersAsync(firstByte[0], cancellationToken);
        if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, out var contentLength))
            throw new InvalidOperationException("Missing Content-Length header");

        var payload = new byte[contentLength];
        var offset = 0;

        while (offset < contentLength)
        {
            var read = await _input.ReadAsync(payload.AsMemory(offset, contentLength - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while reading payload.");
            offset += read;
        }

        return Encoding.UTF8.GetString(payload);
    }

    public Task WriteResultAsync(JsonElement id, object result, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);

    public Task WriteErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken) =>
        WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_lineFraming == true)
            {
                await _output.WriteAsync(bytes, cancellationToken);
                await _output.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken);
            }
            else
            {
                var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
                await _output.WriteAsync(header, cancellationToken);
                await _output.WriteAsync(bytes, cancellationToken);
            }

            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<string> ReadLineFramedJsonAsync(byte firstByte, CancellationToken cancellationToken)
    {
        var buffer = new List<byte> { firstByte };
        var one = new byte[1];

        while (true)
        {
            var n = await _input.ReadAsync(one.AsMemory(0, 1), cancellationToken);
            if (n == 0)
                break;

            if (one[0] == (byte)'\n')
                break;

            if (one[0] != (byte)'\r')
                buffer.Add(one[0]);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
    }

    private async Task<Dictionary<string, string>> ReadHeadersAsync(byte firstByte, CancellationToken cancellationToken)
    {
        var buffer = new List<byte> { firstByte };
        var one = new byte[1];

        while (true)
        {
            var read = await _input.ReadAsync(one.AsMemory(0, 1), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while reading headers.");

            buffer.Add(one[0]);
            if (HasHeaderTerminator(buffer))
                break;
        }

        var text = Encoding.ASCII.GetString(buffer.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }

        return headers;
    }

    private static bool HasHeaderTerminator(List<byte> bytes)
    {
        var n = bytes.Count;
        if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
            return true;

        if (n >= 2 && bytes[n - 2] == '\n' && bytes[n - 1] == '\n')
            return true;

        return false;
    }
}
