// Models/AlprResponse.cs
// Internal DTO used by SatpaClient to deserialize the JSON response
// from the FastAPI /recognize endpoint.
//
// FastAPI response shape:
// {
//   "success":        true,
//   "plate":          "12 ب 345 ایران 67",
//   "plate_raw":      "12ب34567",
//   "valid":          true,
//   "confidence":     0.924,
//   "ocr_chars":      8,
//   "api_latency_ms": 87
// }

using System.Text.Json.Serialization;

namespace mYPMS.Models;

/// <summary>
/// Mirrors the JSON body returned by POST /recognize on the Iranian ALPR FastAPI service.
/// All property names use snake_case to match the Python API output directly —
/// System.Text.Json deserializes them via <see cref="JsonPropertyNameAttribute"/>.
/// </summary>
internal sealed class AlprResponse
{
    /// <summary>True when the API processed the image without error.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Formatted Persian plate string, e.g. "12 ب 345 ایران 67".
    /// Null or empty when <see cref="Success"/> is false or no plate was found.
    /// </summary>
    [JsonPropertyName("plate")]
    public string? Plate { get; init; }

    /// <summary>
    /// Raw concatenated characters without formatting, e.g. "12ب34567".
    /// </summary>
    [JsonPropertyName("plate_raw")]
    public string? PlateRaw { get; init; }

    /// <summary>True when the plate string passes the Iranian format regex.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    /// <summary>Detection confidence score from the plate-detector model (0–1).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>
    /// Number of characters recognised by the OCR model.
    /// A valid Iranian plate has exactly 8 characters.
    /// </summary>
    [JsonPropertyName("ocr_chars")]
    public int OcrChars { get; init; }

    /// <summary>End-to-end API processing latency in milliseconds.</summary>
    [JsonPropertyName("api_latency_ms")]
    public int ApiLatencyMs { get; init; }
}
