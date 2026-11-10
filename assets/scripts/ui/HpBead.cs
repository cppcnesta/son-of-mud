using Godot;

namespace SonOfMud.UI;

/// <summary>
/// 一颗泥珠：按比例从下往上填充（Amount / Capacity）。从有血掉到空时播放碎裂效果
/// （碎裂贴图横排 N 帧，在 _Draw 里按帧画在珠子上面）。
/// 由 HpBeadBar 创建和设置，不自己订阅事件。
/// 注意：这里不在 C# 里 new 任何 RefCounted 资源（如 AtlasTexture），避免 C# 终结器和引擎释放之间的竞争。
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
    public bool BreakPlaying { get; private set; }

    private Texture2D _breakSheet;
    private int _breakFrames = 1;
    private int _breakFrame;
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
        _breakSheet = breakSheet;
        _breakFrames = Mathf.Max(1, breakFrames);
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
        if (!IsInsideTree()) return;
        _breakTween?.Kill();
        BreakPlaying = true;
        SetBreakFrame(0);
        _breakTween = CreateTween();
        _breakTween.TweenMethod(Callable.From<int>(SetBreakFrame), 0, _breakFrames - 1, Mathf.Max(seconds, 0.01f));
        _breakTween.TweenCallback(Callable.From(EndBreak));
    }

    private void EndBreak()
    {
        BreakPlaying = false;
        QueueRedraw();
    }

    private void SetBreakFrame(int frame)
    {
        _breakFrame = Mathf.Clamp(frame, 0, _breakFrames - 1);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!BreakPlaying || _breakSheet == null) return;
        float frameWidth = _breakSheet.GetWidth() / (float)_breakFrames;
        var source = new Rect2(frameWidth * _breakFrame, 0, frameWidth, _breakSheet.GetHeight());
        DrawTextureRectRegion(_breakSheet, new Rect2(Vector2.Zero, Size), source);
    }
}
