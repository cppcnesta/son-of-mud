using Godot;

namespace SonOfMud.UI;

/// <summary>
/// 一颗泥珠：按比例从下往上填充（Amount / Capacity）。从有血掉到空时播放碎裂效果。
/// 贴图、填充方向都在 hud.tscn 的模板珠子 BeadTemplate 上设置好；碎裂是它的子节点 Break（Sprite2D，横排 N 帧）。
/// 由 HpBeadBar 复制模板创建并设置，不自己订阅事件。C# 里不引用任何贴图资源（原因见 UiSettings.Shared）。
/// </summary>
public partial class HpBead : TextureProgressBar
{
    /// <summary>这颗珠子能装的血量（通常 20；最大血量不是 20 的倍数时最后一颗小一些）。</summary>
    public int Capacity { get; private set; } = 1;
    /// <summary>这颗珠子应当显示的血量（动画的目标）。</summary>
    public int Amount { get; private set; }
    /// <summary>目标填充比例 0..1。</summary>
    public float Fill => Capacity > 0 ? (float)Amount / Capacity : 0f;
    /// <summary>当前画面上的填充比例（动画进行中会和 Fill 不同）。</summary>
    public float DisplayedFill => MaxValue > 0 ? (float)(Value / MaxValue) : 0f;
    /// <summary>播放过几次碎裂效果。</summary>
    public int BreakCount { get; private set; }
    public bool BreakPlaying => _break != null && _break.Visible;

    private Sprite2D _break;
    private Tween _fillTween;
    private Tween _breakTween;

    public void Setup(int capacity, Vector2 size)
    {
        Step = 0;
        MinValue = 0;
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        Capacity = Mathf.Max(1, capacity);
        MaxValue = Capacity;
        Amount = 0;
        Value = 0;
        _break = GetNodeOrNull<Sprite2D>("Break");
        if (_break != null) _break.Visible = false;
    }

    /// <summary>设置目标血量。animate=false 时立刻到位；animate=true 且从有血掉到空时播放碎裂。</summary>
    public void SetAmount(int amount, bool animate, float seconds, float breakSeconds)
    {
        amount = Mathf.Clamp(amount, 0, Capacity);
        bool broke = Amount > 0 && amount == 0;
        Amount = amount;
        _fillTween?.Kill();
        if (!animate || seconds <= 0f || !IsInsideTree())
        {
            Value = amount;
        }
        else
        {
            _fillTween = CreateTween();
            _fillTween.TweenProperty(this, "value", (double)amount, seconds);
        }
        if (broke && animate) PlayBreak(breakSeconds);
    }

    private void PlayBreak(float seconds)
    {
        BreakCount++;
        if (_break == null || !IsInsideTree()) return;
        _breakTween?.Kill();
        _break.Frame = 0;
        _break.Visible = true;
        int last = Mathf.Max(1, _break.Hframes) - 1;
        _breakTween = CreateTween();
        _breakTween.TweenProperty(_break, "frame", last, Mathf.Max(seconds, 0.01f));
        _breakTween.TweenCallback(Callable.From(() => _break.Visible = false));
    }
}
