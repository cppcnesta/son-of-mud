using Godot;

namespace SonOfMud.Player;

/// <summary>土狼时间：从地面走下去（不是起跳离地）后开始计时，期间按跳仍算地面跳。</summary>
public sealed class CoyoteTimer
{
    public float Remaining { get; private set; }
    public bool Active => Remaining > 0f;

    public void Start(float seconds) => Remaining = Mathf.Max(0f, seconds);

    public void Tick(float delta)
    {
        Remaining = Mathf.Max(0f, Remaining - delta);
        if (Remaining < 0.000001f) Remaining = 0f;
    }

    public void Clear() => Remaining = 0f;
}
