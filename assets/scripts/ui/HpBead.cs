using Godot;

namespace SonOfMud.UI;

/// <summary>
/// 一颗泥珠：按比例从下往上填充（Amount / Capacity）。从有血掉到空时播放碎裂效果。
/// 由 HpBeadBar 创建和设置，不自己订阅事件。
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

    private TextureRect _break;
    private AtlasTexture _breakFrame;
    private int _breakFrames = 1;
    private Tween _fillTween;
    private Tween _breakTween;

    public void Setup(int capacity, int amount, Texture2D full, Texture2D empty, Texture2D breakSheet, int breakFrames, Vector2 size)
    {
        Step = 0;
        MinValue = 0;
        FillMode = (int)FillModeEnum.BottomToTop;
        TextureProgress = full;
        TextureUnder = empty;
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        Capacity = Mathf.Max(1, capacity);
        MaxValue = Capacity;
        Amount = Mathf.Clamp(amount, 0, Capacity);
        Value = Amount;

        _breakFrames = Mathf.Max(1, breakFrames);
        if (breakSheet == null) return;
        Vector2 frameSize = new(breakSheet.GetWidth() / (float)_breakFrames, breakSheet.GetHeight());
        _breakFrame = new AtlasTexture { Atlas = breakSheet, Region = new Rect2(Vector2.Zero, frameSize) };
        _break = new TextureRect
        {
            Name = "Break",
            Texture = _breakFrame,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        AddChild(_break);
        _break.SetAnchorsPreset(LayoutPreset.FullRect);
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
        _break.Visible = true;
        SetBreakFrame(0);
        _breakTween = CreateTween();
        _breakTween.TweenMethod(Callable.From<int>(SetBreakFrame), 0, _breakFrames - 1, Mathf.Max(seconds, 0.01f));
        _breakTween.TweenCallback(Callable.From(() => _break.Visible = false));
    }

    private void SetBreakFrame(int frame)
    {
        if (_breakFrame == null) return;
        Vector2 size = _breakFrame.Region.Size;
        _breakFrame.Region = new Rect2(new Vector2(size.X * Mathf.Clamp(frame, 0, _breakFrames - 1), 0), size);
    }
}
