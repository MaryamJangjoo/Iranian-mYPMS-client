// SATPA.cs — Typed HttpClient for the Iranian ALPR FastAPI service.
//
// Registration in Program.cs:
//   builder.Services.Configure<AlprOptions>(builder.Configuration.GetSection("Alpr"));
//   builder.Services.AddHttpClient<SatpaClient>();
//
// Injected into HomeController via constructor.

using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using mYPMS.Models;

namespace mYPMS.Models;

/// <summary>
/// Typed HttpClient that calls the Iranian ALPR FastAPI service.
/// Handles image upload, response deserialization, confidence gating,
/// character-count gating, and plate normalization.
/// </summary>
public sealed class SatpaClient
{
    private readonly HttpClient           _http;
    private readonly AlprOptions          _opt;
    private readonly ILogger<SatpaClient> _logger;

    public SatpaClient(
        HttpClient            http,
        IOptions<AlprOptions> opt,
        ILogger<SatpaClient>  logger)
    {
        _opt    = opt.Value;
        _logger = logger;

        http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        http.Timeout     = TimeSpan.FromSeconds(
            _opt.TimeoutSeconds > 0 ? _opt.TimeoutSeconds : 10);

        _http = http;
    }

    // ── Health check ──────────────────────────────────────────────────────────

    /// <summary>Returns true if the ALPR service is reachable and healthy.</summary>
    public async Task<bool> IsAliveAsync()
    {
        try
        {
            var r = await _http.GetAsync("health").ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ALPR health check failed");
            return false;
        }
    }

    // ── Main recognition call ─────────────────────────────────────────────────

    /// <summary>
    /// Upload raw image bytes to the ALPR API and return a parsed result.
    /// </summary>
    /// <param name="image">Raw image bytes (JPEG, PNG, or BMP).</param>
    /// <param name="fileName">File name sent in the multipart form — used for logging.</param>
    public async Task<AlprResult> RecognizeAsync(
        byte[] image,
        string fileName = "plate.jpg")
    {
        if (image == null || image.Length == 0)
            return Fail("empty image");

        using var form        = new MultipartFormDataContent();
        var       fileContent = new ByteArrayContent(image);

        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : "image/jpeg");

        form.Add(fileContent, "file", fileName);

        try
        {
            using var response = await _http
                .PostAsync("recognize", form)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var data = await response.Content
                .ReadFromJsonAsync<AlprResponse>()
                .ConfigureAwait(false);

            if (data is null || !data.Success)
                return Fail("API returned success=false or null body");

            // ── Confidence gate ───────────────────────────────────────────────
            if (data.Confidence < _opt.MinConfidence)
            {
                _logger.LogWarning(
                    "ALPR low confidence {Conf:F2} < threshold {Min:F2} for plate '{Plate}'",
                    data.Confidence, _opt.MinConfidence, data.Plate);
                return Fail($"low confidence ({data.Confidence:F2})");
            }

            // ── Character-count gate ──────────────────────────────────────────
            if (data.OcrChars != 8)
            {
                _logger.LogWarning(
                    "ALPR invalid char count {Count} for plate '{Plate}'",
                    data.OcrChars, data.Plate);
                return Fail($"invalid char count ({data.OcrChars})");
            }
            var plate = Normalize(data.Plate ?? string.Empty);
            _logger.LogInformation(
                "ALPR OK: {Plate}  conf={Conf:F2}  latency={Lat}ms",
                plate, data.Confidence, data.ApiLatencyMs);

            return new AlprResult
            {
                Success      = true,
                Plate        = plate,
                Confidence   = data.Confidence,
                CharCount    = data.OcrChars,
                ErrorMessage = string.Empty,
            };
        }
        catch (TaskCanceledException)
        catch (HttpRequestException ex)
        {
            return Fail($"network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SatpaClient.RecognizeAsync threw unexpectedly");
            return Fail(ex.Message);
        }
    }

    // ── File-path overload ────────────────────────────────────────────────────

    /// <summary>Read an image from disk and call <see cref="RecognizeAsync"/>.</summary>
    public async Task<AlprResult> RecognizeFileAsync(string fullPath)
    {
        if (!File.Exists(fullPath))
            return Fail($"file not found: {fullPath}");

        var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        return await RecognizeAsync(bytes, Path.GetFileName(fullPath)).ConfigureAwait(false);
    }

    // ── Plate normalization ───────────────────────────────────────────────────

    /// <summary>
    /// Convert Persian-extended digits to ASCII and strip noise tokens.
    /// Persian-extended digit range: U+06F0–U+06F9
    /// </summary>
    private static string Normalize(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return string.Empty;

        return plate
            .Replace('\u06f0', '0').Replace('\u06f1', '1')
            .Replace('\u06f2', '2').Replace('\u06f3', '3')
            .Replace('\u06f4', '4').Replace('\u06f5', '5')
            .Replace('\u06f6', '6').Replace('\u06f7', '7')
            .Replace('\u06f8', '8').Replace('\u06f9', '9')
            .Replace("ایران", string.Empty)
            .Replace("IRAN",  string.Empty)
            .Replace("IR",    string.Empty)
            .Replace("\0",    string.Empty)
            .Trim();
    }

    // ── Failure helper ────────────────────────────────────────────────────────

    private AlprResult Fail(string message)
    {
        _logger.LogWarning("SatpaClient: {Msg}", message);
        return new AlprResult
        {
            Success      = false,
            Plate        = string.Empty,
            Confidence   = -1,
            CharCount    = 0,
            ErrorMessage = message,
        };
    }
}        {
            return Fail($"timeout after {_opt.TimeoutSeconds}s");
        }

