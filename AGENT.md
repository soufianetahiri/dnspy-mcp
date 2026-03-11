# AGENT.md — dnSpy MCP Server (C#)

This document explains **every major aspect** of this MCP server so another agent (or human) can operate, extend, and troubleshoot it safely.

---

## 1) Purpose

`dnspy-mcp` is a local MCP (Model Context Protocol) server for .NET reverse engineering.

It provides tools to:
- inspect assemblies (`list_types`, `list_methods`, `search_members`)
- decompile code (`decompile_type`, `decompile_method`)
- inspect IL (`get_method_il`, `find_string_references`)
- generate dnSpy navigation instructions (`format_dnspy_jump`)
- patch binaries (`patch_replace_string_literal`, `patch_nop_instructions`)

It does **not** require the dnSpy app at runtime. It uses:
- `dnlib`
- `ICSharpCode.Decompiler`

---

## 2) MCP transport + protocol

Transport: **stdio JSON-RPC**.

Supported framing:
- LSP-style headers: `Content-Length: N\r\n\r\n<json>`
- line framed JSON (single JSON message per line)

Implemented MCP methods:
- `initialize`
- `notifications/initialized`
- `ping`
- `tools/list`
- `tools/call`
- `resources/list`
- `resources/read`

Protocol version used: `2024-11-05`.

---

## 3) Project layout

- `src/DnSpyMcpServer/Program.cs`
  - app entrypoint
- `Core/DnSpyMcpHost.cs`
  - dependency wiring (rpc, registry, services)
- `Core/McpServer.cs`
  - request router (`initialize`, `tools/*`, `resources/*`)
- `Transport/StdioJsonRpc.cs`
  - stdio read/write + framing logic
- `Tools/*`
  - attribute-based tool registration and tool implementations
- `Services/AssemblyAnalysisService.cs`
  - decompilation, searching, IL extraction, patching
- `Services/ResourceRegistry.cs`
  - MCP resources implementation

---

## 4) Tool registration model

Tools are discovered via reflection:
- methods in `Tools/DnSpyTools.cs`
- marked with `[McpTool(name, description)]`
- parameters described with `[ToolParam(description)]`

`ToolRegistry` generates MCP JSON schema from C# signatures.

First parameter of every tool must be `ToolContext`.

---

## 5) Detailed tool reference

### Analysis / navigation tools

1. `list_types`
2. `decompile_type`
3. `decompile_method`
4. `get_method_il`
5. `search_members`
6. `list_methods`
7. `find_string_references`
8. `format_dnspy_jump`

Token-rich output is intentional:
- includes `TypeDef`, `MethodDef`, etc.
- allows direct jump in dnSpy or precise patch target selection

### Patch tools

9. `patch_replace_string_literal`
- target by `methodDefToken` + `ilOffset`
- replaces string literal operand at that IL instruction

10. `patch_nop_instructions`
- target by `methodDefToken` + `ilOffset`
- NOPs `count` instructions from that offset

#### Patch safety rule

**Backups are always created before any patch**.

For each patch call:
- source file is copied to `sourcePath.yyyyMMdd_HHmmss.bak`
- patch is then written to destination

Destination behavior:
- if `inPlace = true`: destination = source file
- else destination = `outputPath` if provided, or `<name>.patched.<ext>`

---

## 6) Resources

`resources/list`:
- `dnspy://assemblies`
- per-cached-assembly resources for summary/types

`resources/read` supports:
- `dnspy://assemblies`
- `dnspy://assembly?path=<...>&view=summary`
- `dnspy://assembly?path=<...>&view=types`

Cache is populated when tools load assemblies by `assemblyPath`.

---

## 7) Typical agent workflows

### A) Locate popup string, then jump in dnSpy

1) `find_string_references` with literal fragment
2) take returned `MethodDef`, `IL_xxxx`
3) `format_dnspy_jump` using those tokens/offset

### B) Patch popup text

1) find `MethodDef` + `IL` line with literal
2) call `patch_replace_string_literal`
3) inspect output path + backup path
4) validate by re-running `find_string_references` on patched file

### C) Suppress call path (advanced)

1) locate call-site in IL
2) call `patch_nop_instructions` with suitable `count`
3) verify control flow manually in dnSpy

---

## 8) Build, run, publish

Build:
```bash
dotnet build src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release
```

Run:
```bash
dotnet run --project src/DnSpyMcpServer/DnSpyMcpServer.csproj -c Release
```

Publish profiles:
- `win-x64`
- `linux-x64`

---

## 9) OpenCode config (Windows)

Config file:
- `%USERPROFILE%\.config\opencode\opencode.json`

Use local command format:
```json
"dnspy": {
  "type": "local",
  "enabled": true,
  "command": [
    "dotnet",
    "C:/.../DnSpyMcpServer.dll"
  ]
}
```

---

## 10) Known caveats

- Patching can break signatures/strong-name expectations in some apps.
- NOP patching can break control flow if applied blindly.
- Always validate patched binaries in isolated test environments.
- If build fails with file-lock errors, stop running MCP process first.

---

## 11) Extension guidance

Recommended additions:
- patch dry-run mode (show modifications without writing)
- patch plan tool (`patch_in_order_to`) returning deterministic steps
- call graph helpers for upstream condition tracing
- safe patch templates for common scenarios

When adding tools:
1) add method in `DnSpyTools.cs`
2) implement logic in service
3) ensure token-rich output
4) update README + this AGENT.md
