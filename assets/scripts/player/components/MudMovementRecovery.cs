using Godot;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>One movement recovery channel. A new action replaces the previous recovery.</summary>
public sealed class MudMovementRecovery
{
    public enum Source { None, Dodge, Attack }
    public Source RecoverySource { get; private set; }
    public float Remaining { get; set; }
    public float Duration { get; private set; }
    public float InitialMultiplier { get; private set; } = 0.2f;
    public Curve RecoveryCurve { get; private set; }

    public void Start(Source source, float seconds, float initial, Curve curve)
    {
        RecoverySource = source;
        Duration = Mathf.Max(0f, seconds);
        Remaining = Duration;
        InitialMultiplier = Mathf.Clamp(initial, 0f, 1f);
        RecoveryCurve = curve;
        if (Duration <= 0f) Clear();
    }
    public void Tick(float delta)
    {
        Remaining = Mathf.Max(0f, Remaining - delta);
        if (Remaining < 0.000001f) Clear();
    }
    public float Multiplier()
    {
        if (Remaining <= 0f || Duration <= 0f) return 1f;
        var elapsed = Mathf.Clamp(1f - Remaining / Duration, 0f, 1f);
        var recovered = RecoveryCurve != null ? RecoveryCurve.Sample(elapsed) : 1f - Mathf.Pow(1f - elapsed, 2f);
        return Mathf.Lerp(InitialMultiplier, 1f, Mathf.Clamp(recovered, 0f, 1f));
    }
    public void Clear() { RecoverySource = Source.None; Remaining = 0f; Duration = 0f; }
    public GDictionary Snapshot() => new()
    {
        ["source"] = (int)RecoverySource, ["remaining"] = Remaining, ["duration"] = Duration,
        ["initial"] = InitialMultiplier, ["curve_path"] = RecoveryCurve?.ResourcePath ?? ""
    };
    public void Restore(GDictionary data, Curve fallbackCurve)
    {
        var curve = fallbackCurve;
        var path = data.Str("curve_path");
        if (path.Length > 0 && ResourceLoader.Exists(path, "Curve")) curve = GD.Load<Curve>(path);
        var value = data.Int("source");
        var source = value == 2 ? Source.Attack : value == 1 ? Source.Dodge : Source.None;
        Start(source, data.Num("duration"), data.Num("initial", 0.2f), curve);
        Remaining = Mathf.Clamp(data.Num("remaining"), 0f, Duration);
        if (Remaining <= 0f) Clear();
    }
}
