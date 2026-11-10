using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Player;

/// <summary>
/// 移动减速恢复通道（闪避后、泥攻击后摇）。只有一条通道：新的动作会替换旧的恢复。
/// 倍率从 InitialMultiplier 沿曲线恢复到 1（默认 2t−t²：20%→55%→80%→95%→100%）。
/// </summary>
public sealed class MovementRecovery
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
        float elapsed = Mathf.Clamp(1f - Remaining / Duration, 0f, 1f);
        float recovered = RecoveryCurve != null ? RecoveryCurve.Sample(elapsed) : 1f - Mathf.Pow(1f - elapsed, 2f);
        return Mathf.Lerp(InitialMultiplier, 1f, Mathf.Clamp(recovered, 0f, 1f));
    }

    public void Clear()
    {
        RecoverySource = Source.None;
        Remaining = 0f;
        Duration = 0f;
    }

    public GDictionary Snapshot() => new()
    {
        ["source"] = (int)RecoverySource,
        ["remaining"] = Remaining,
        ["duration"] = Duration,
        ["initial"] = InitialMultiplier,
    };

    public void Restore(GDictionary data, Curve curve)
    {
        if (data == null) { Clear(); return; }
        var source = (Source)(data.TryGetValue("source", out var s) ? s.AsInt32() : 0);
        float duration = data.TryGetValue("duration", out var d) ? d.AsSingle() : 0f;
        float initial = data.TryGetValue("initial", out var i) ? i.AsSingle() : 0.2f;
        Start(source, duration, initial, curve);
        Remaining = Mathf.Clamp(data.TryGetValue("remaining", out var r) ? r.AsSingle() : 0f, 0f, Duration);
        if (Remaining <= 0f) Clear();
    }
}
