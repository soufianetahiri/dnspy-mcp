# dnSpy MCP Server (C#)

MCP server for .NET reverse engineering workflows (dnSpy/ILSpy ecosystem), built in C#.

## What’s implemented

- Modular architecture (`Core`, `Transport`, `Tools`, `Services`)
- Reflection-based tool registration via attributes
- Auto-generated tool JSON schemas from C# method signatures
- MCP stdio transport (Claude Code / Codex / OpenCode compatible)
- MCP `resources/list` + `resources/read`
- Structured tool outputs (`structuredContent`) + plain text content
- Single-file publish profiles (win-x64 / linux-x64)

For full implementation details, see `AGENT.md`.

## Project layout

- `dnspy-mcp.slnx`
- `Directory.Build.props`
- `publish.ps1`
- `src/DnSpyMcpServer/Program.cs`
- `src/DnSpyMcpServer/Core/*`
- `src/DnSpyMcpServer/Transport/*`
- `src/DnSpyMcpServer/Tools/*`
- `src/DnSpyMcpServer/Services/*`
- `src/DnSpyMcpServer/Properties/PublishProfiles/*`

## Tools

- `list_types`
- `decompile_type`
- `decompile_method` (supports overload targeting)
- `get_method_il` (supports overload targeting)
- `search_members`
- `list_methods` (helper for overload signatures)
- `find_string_references` (find string-literal references in IL)
- `format_dnspy_jump` (turn tokens/IL offsets into direct dnSpy navigation steps)
- `patch_replace_string_literal` (patch one IL string literal)
- `patch_nop_instructions` (NOP one/many IL instructions)

Navigation-friendly output: search/method/reference results include metadata tokens (`TypeDef`, `MethodDef`, etc.) so you can jump directly in dnSpy by token.

Patch safety: patch tools **always create a backup** before writing changes.

All tools return:
- `content` (text)
- `structuredContent` (JSON object for programmatic clients)

## Resources

- `resources/list` returns:
  - `dnspy://assemblies`
  - cached assembly resources, e.g. `dnspy://assembly?path=...&view=summary`
  - cached assembly resources, e.g. `dnspy://assembly?path=...&view=types`

- `resources/read` supports:
  - `dnspy://assemblies`
  - `dnspy://assembly?path=<absolute-or-relative>&view=summary`
  - `dnspy://assembly?path=<absolute-or-relative>&view=types`

> Note: assembly resources are cache-driven. Cache is populated when tools are called with `assemblyPath`.

## Build

```bash
dotnet build src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release
```

## Run

```bash
dotnet run --project src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release
```

## Publish (single-file)

PowerShell helper:

```powershell
./publish.ps1 -Runtime win-x64
./publish.ps1 -Runtime linux-x64
```

Or directly:

```bash
dotnet publish src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release -p:PublishProfile=win-x64
dotnet publish src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release -p:PublishProfile=linux-x64
```

## Quick start (copy/paste)

1. Build:

```bash
dotnet build src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release
```

2. Use this server command in your MCP client:

```json
{
  "mcpServers": {
    "dnspy": {
      "command": "dotnet",
      "args": [
        "C:/Tools/Cooking/Reverse/dnSpy/dnspy-mcp/src/DnSpyMcpServer/bin/Release/net8.0/win-x64/DnSpyMcpServer.dll"
      ]
    }
  }
}
```

3. Restart your MCP client.

## Prebuilt config files

Ready-to-copy templates are in:

- `configs/claude-code.mcp.json`
- `configs/codex.mcp.json`
- `configs/opencode.mcp.json`

> If your local path differs, edit the executable path in the selected file (`args[0]` for stdio clients, `command[0]` for OpenCode local MCP).

## Client-specific config snippets

### Claude Code

Add/update your MCP config with:

```json
{
  "mcpServers": {
    "dnspy": {
      "command": "dotnet",
      "args": [
        "C:/Tools/Cooking/Reverse/dnSpy/dnspy-mcp/src/DnSpyMcpServer/bin/Release/net8.0/win-x64/DnSpyMcpServer.dll"
      ]
    }
  }
}
```

### Codex

Use the same MCP server entry:

```json
{
  "mcpServers": {
    "dnspy": {
      "command": "dotnet",
      "args": [
        "C:/Tools/Cooking/Reverse/dnSpy/dnspy-mcp/src/DnSpyMcpServer/bin/Release/net8.0/win-x64/DnSpyMcpServer.dll"
      ]
    }
  }
}
```

### OpenCode (Windows)

OpenCode uses this config file:

`%USERPROFILE%\.config\opencode\opencode.json`

Add this block under `mcp`:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "dnspy": {
      "type": "local",
      "enabled": true,
      "command": [
        "dotnet",
        "C:/Tools/Cooking/Reverse/dnSpy/dnspy-mcp/src/DnSpyMcpServer/bin/Release/net8.0/win-x64/DnSpyMcpServer.dll"
      ]
    }
  }
}
```

(Template file: `configs/opencode.mcp.json`)

### Optional: run from project instead of DLL

```json
{
  "mcpServers": {
    "dnspy": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Tools/Cooking/Reverse/dnSpy/dnspy-mcp/src/DnSpyMcpServer/DnSpyMcpServer.csproj",
        "-c",
        "Release"
      ]
    }
  }
}
```

## First actions to test in your client

After restart, ask your client to call:

1. `list_types`
```json
{ "assemblyPath": "C:/path/to/target.dll" }
```

2. `list_methods`
```json
{ "assemblyPath": "C:/path/to/target.dll", "typeFullName": "MyApp.Services.AuthService" }
```

3. `decompile_method`
```json
{
  "assemblyPath": "C:/path/to/target.dll",
  "typeFullName": "MyApp.Services.AuthService",
  "methodName": "Login",
  "parameterTypeNames": ["System.String", "System.String"]
}
```

4. `resources/list` then `resources/read` with `dnspy://assemblies`

5. `find_string_references`
```json
{
  "assemblyPath": "C:/Program Files (x86)/Cato Networks/Cato Client/CatoClient.exe",
  "text": "Screenshot 2026-03-11 082704.png",
  "caseSensitive": false,
  "maxResults": 200
}
```

Tip: also search fragments like `Pictures\\Screenshots` or just `Screenshot`.

6. `format_dnspy_jump`
```json
{
  "assemblyPath": "C:/Program Files (x86)/Cato Networks/Cato Client/CatoClient.exe",
  "typeDefToken": "0x02000058",
  "methodDefToken": "0x060005C1",
  "ilOffset": "IL_01D2"
}
```

7. `patch_replace_string_literal` (always creates backup)
```json
{
  "assemblyPath": "C:/Program Files (x86)/Cato Networks/Cato Client/CatoClient.exe",
  "methodDefToken": "0x060005C1",
  "ilOffset": "IL_01D2",
  "newText": "[patched text]",
  "inPlace": false
}
```

8. `patch_nop_instructions` (always creates backup)
```json
{
  "assemblyPath": "C:/Program Files (x86)/Cato Networks/Cato Client/CatoClient.exe",
  "methodDefToken": "0x060005C1",
  "ilOffset": "IL_01DD",
  "count": 1,
  "inPlace": false
}
```

## Overload targeting example

```json
{
  "assemblyPath": "C:/path/target.dll",
  "typeFullName": "MyApp.Services.AuthService",
  "methodName": "Login",
  "parameterTypeNames": ["System.String", "System.String"]
}
```

If `parameterTypeNames` is omitted and overloads exist, the server returns an ambiguity error with available signatures.
