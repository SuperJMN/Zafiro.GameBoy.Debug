using System.Text.Json.Serialization;
using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Mcp;

public sealed record ToolError([property: JsonPropertyName("error")] DebugError Error);
