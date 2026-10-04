using System.Text.Json.Serialization;

namespace Zafiro.GameBoy.Debug.Core;

public sealed record MemoryReadResult(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("bytesHex")] string BytesHex,
    [property: JsonPropertyName("bytes")] byte[] Bytes,
    [property: JsonPropertyName("ascii")] string Ascii);
