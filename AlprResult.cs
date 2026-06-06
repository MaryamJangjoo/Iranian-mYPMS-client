namespace mYPMS.Models;

public class AlprResult
{
    public bool   Success      { get; set; }
    public string Plate        { get; set; } = string.Empty;
    public double Confidence   { get; set; }
    public int    CharCount    { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public override string ToString() =>
        Success
            ? $"[OK] {Plate}  conf={Confidence:F2}  chars={CharCount}"
            : $"[FAIL] {ErrorMessage}";
}