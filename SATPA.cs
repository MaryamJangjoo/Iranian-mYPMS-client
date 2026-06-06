using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace mYPMS.Models;

// SatpaClient — typed HttpClient for FastAPI ALPR server
// Registered in Program.cs:
//   builder.Services.AddHttpClient<SatpaClient>();
//   builder.Services.Configure<AlprOptions>(builder.Configuration.GetSection("Alpr"));
//
// Injected into HomeController via constructor.
// Replaces static Satpa.cs — fully async, no deadlock risk.

public class SatpaClient
{
    private readonly HttpClient  _http;
    private readonly AlprOptions _opt;
    private readonly ILogger<SatpaClient> _logger;

    public SatpaClient(
        HttpClient http,
        IOptions<AlprOptions> opt,
        ILogger<SatpaClient> logger)
    {
        _opt    = opt.Value;
        _logger = logger;

        http.BaseAddress = new Uri(_opt.BaseUrl);
        http.Timeout     = TimeSpan.FromSeconds(_opt.TimeoutSeconds);
        _http = http;
    }

    // ── Health check ─────────────────────────────────────────────────────────

    public async Task<bool> IsAliveAsync()
    {
        try
        {
            var r = await _http.GetAsync("/health");
            return r.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Main recognition call ─────────────────────────────────────────────────

    public async Task<AlprResult> RecognizeAsync(
        byte[]  image,
        string  fileName = "plate.jpg")
    {
        if (image == null || image.Length == 0)
            return Fail("empty image");

        using var form        = new MultipartFormDataContent();
        var       fileContent = new ByteArrayContent(image);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "file", fileName);

        try
        {
            var response = await _http.PostAsync("/recognize", form);

            if (!response.IsSuccessStatusCode)
                return Fail($"HTTP {(int)response.StatusCode}");

            var data = await response.Content
                                     .ReadFromJsonAsync<AlprResponse>();

            if (data == null || !data.success)
                return Fail("invalid response from API");

            // ── Confidence gate ───────────────────────────────────────────────
            if (data.confidence < _opt.MinConfidence)
            {
                _logger.LogWarning(
                    "ALPR low confidence {Conf:F2} < {Min:F2} for plate '{Plate}'",
                    data.confidence, _opt.MinConfidence, data.plate);
                return Fail($"low confidence ({data.confidence:F2})");
            }

            // ── Length gate ───────────────────────────────────────────────────
            if (data.ocr_chars != 8)
            {
                _logger.LogWarning(
                    "ALPR invalid char count {Count} for plate '{Plate}'",
                    data.ocr_chars, data.plate);
                return Fail($"invalid char count ({data.ocr_chars})");
            }

            var plate = Normalize(data.plate);
            _logger.LogInformation("ALPR OK: {Plate}  conf={Conf:F2}", plate, data.confidence);

            return new AlprResult
            {
                Success      = true,
                Plate        = plate,
                Confidence   = data.confidence,
                CharCount    = data.ocr_chars,
                ErrorMessage = string.Empty,
            };
        }
        catch (TaskCanceledException)
        {
            return Fail($"timeout after {_opt.TimeoutSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SatpaClient.RecognizeAsync threw");
            return Fail(ex.Message);
        }
    }

    // ── Convenience overload: read from file path ─────────────────────────────

    public async Task<AlprResult> RecognizeFileAsync(string fullPath)
    {
        if (!System.IO.File.Exists(fullPath))
            return Fail($"file not found: {fullPath}");

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
        return await RecognizeAsync(bytes, Path.GetFileName(fullPath));
    }

    // ── Normalize plate string ────────────────────────────────────────────────
    // Converts Persian digits to ASCII, strips noise tokens (IRAN, IR).

    private static string Normalize(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return string.Empty;

        return plate
            .Replace("\u06f0", "0").Replace("\u06f1", "1")
            .Replace("\u06f2", "2").Replace("\u06f3", "3")
            .Replace("\u06f4", "4").Replace("\u06f5", "5")
            .Replace("\u06f6", "6").Replace("\u06f7", "7")
            .Replace("\u06f8", "8").Replace("\u06f9", "9")
            .Replace("\u0627\u06cc\u0631\u0627\u0646", "")  // ایران
            .Replace("IRAN", "")
            .Replace("IR", "")
            .Replace("\0", "")
            .Trim();
    }

    // ── Fail helper ───────────────────────────────────────────────────────────

    private AlprResult Fail(string msg)
    {
        _logger.LogWarning("SatpaClient fail: {Msg}", msg);
        return new AlprResult
        {
            Success      = false,
            Plate        = string.Empty,
            Confidence   = -1,
            CharCount    = 0,
            ErrorMessage = msg,
        };
    }
}