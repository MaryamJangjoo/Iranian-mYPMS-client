// Models/AlprOptions.cs
// Strongly-typed configuration bound from appsettings.json → "Alpr" section.
//
// appsettings.json shape:
// "Alpr": {
//   "BaseUrl":        "http://localhost:8000",
//   "MinConfidence":  0.4,
//   "TimeoutSeconds": 10
// }

namespace mYPMS.Models;

/// <summary>
/// Configuration options for the Iranian ALPR FastAPI service.
/// Bound via IOptions&lt;AlprOptions&gt; in Program.cs:
///   builder.Services.Configure&lt;AlprOptions&gt;(builder.Configuration.GetSection("Alpr"));
/// </summary>
public sealed class AlprOptions
{
    /// <summary>Base URL of the FastAPI ALPR service, e.g. "http://localhost:8000".</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Minimum confidence score (0–1) required to accept a plate reading.
    /// Results below this threshold are treated as failures.
    /// </summary>
    public double MinConfidence { get; set; } = 0.4;

    /// <summary>HTTP request timeout in seconds for ALPR calls.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
