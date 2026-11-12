using Godot;

namespace SonOfMud.Enemy;

/// <summary>
/// 敌人场景里的显示节点（名为 Sprite）。游戏里由 <see cref="EnemyAnimator"/> 按状态播放；
/// 编辑器里（只有这个节点是工具脚本，敌人本体不是）按命名显示 idle 图（守坑人是 dormant），摆敌人时就能看到样子，尺寸和脚底对齐与游戏里一致。
/// - 敌人种类从父节点的 Data（EnemyData 资源）的 Kind 读，朝向从父节点的 InitialFacing 读（图画朝左，初始朝右的就翻过来），
///   不直接引用 EnemyBody 类。检查器里改了初始朝向，编辑器里马上跟着翻。
/// - 自动生成的显示只在编辑器里存在：保存场景前还原成原来的样子（不存进场景），保存后再显示。
///   手动填的 SpriteFrames 不是自动生成的那一份，保存时不动，照常存进场景。
/// - 没有 idle 图时什么都不显示（和以前一样只看得到碰撞框）。
/// </summary>
[Tool]
[GlobalClass]
public partial class EnemySprite : AnimatedSprite2D
{
    /// <summary>编辑器里显示的动作：先找 idle，没有再找 dormant（守坑人开场是等待）。</summary>
    public static readonly string[] PreviewAnimations = { "idle", "dormant" };

    /// <summary>编辑器预览找图的文件夹（测试可以改）。</summary>
    public string ArtDirectory { get; set; } = EnemyAnimator.DefaultArtDirectory;
    /// <summary>预览按命名生成的那一份（手动帧时为 null）。</summary>
    public SpriteFrames PreviewFrames { get; private set; }
    public bool PreviewShown { get; private set; }
    /// <summary>编辑器里正在显示的动作（idle / dormant），没显示时为空。</summary>
    public string PreviewAction { get; private set; } = "";

    private bool _hasSnapshot;
    private SpriteFrames _frames;
    private StringName _animation;
    private int _frame;
    private bool _centered;
    private bool _flipH;
    private Vector2 _scale, _offset, _position;

    public override void _Ready()
    {
        if (!Engine.IsEditorHint()) SetProcess(false);
    }

    /// <summary>编辑器里：初始朝向改了就跟着翻（游戏里不处理，朝向归 EnemyAnimator 管）。</summary>
    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint() || !PreviewShown) return;
        bool flip = PreviewFlip();
        if (FlipH != flip) FlipH = flip;
    }

    /// <summary>按父节点的初始朝向算编辑器里要不要翻转（EnemyBody：InitialFacing ≥ 0 是朝右）。</summary>
    public bool PreviewFlip()
    {
        Variant facing = GetParent()?.Get("InitialFacing") ?? default;
        float value = facing.VariantType == Variant.Type.Nil ? -1f : (float)facing.AsDouble();
        return EnemyAnimator.FlipFor(value >= 0f ? 1f : -1f);
    }

    public override void _Notification(int what)
    {
        // 切换场景标签页时编辑器会把场景移出 / 放回场景树，所以按进出场景树显示 / 还原，不用 _Ready（只调一次）
        if (what == NotificationEditorPreSave || what == NotificationExitTree) HidePreview();
        else if ((what == NotificationEditorPostSave || what == NotificationEnterTree) && Engine.IsEditorHint()) ShowPreview();
    }

    /// <summary>在编辑器里显示 idle 图：手动帧用手动的 idle，否则按命名生成（只含 idle）。先记下原来的属性，HidePreview 时原样还原。</summary>
    public void ShowPreview()
    {
        HidePreview();
        Resource data = GetParent()?.Get("Data").AsGodotObject() as Resource;
        string kind = data?.Get("Kind").AsString() ?? "";
        float canvas = data == null ? 128f : (float)data.Get("ArtCanvas").AsDouble();
        if (canvas <= 0f) canvas = 128f;

        SpriteFrames frames = SpriteFrames;
        SpriteFrames generated = null;
        if (frames == null)
        {
            generated = EnemyAnimator.BuildFrames(kind, ArtDirectory, canvas, PreviewAnimations);
            frames = generated;
        }
        string preview = null;
        foreach (string name in PreviewAnimations)
            if (frames != null && frames.HasAnimation(name) && frames.GetFrameCount(name) > 0)
            {
                preview = name;
                break;
            }
        if (preview == null) return;

        _frames = SpriteFrames;
        _animation = Animation;
        _frame = Frame;
        _centered = Centered;
        _scale = Scale;
        _offset = Offset;
        _position = Position;
        _flipH = FlipH;
        _hasSnapshot = true;

        int height = EnemyAnimator.FrameHeight(frames, preview);
        string shown = preview;
        if (generated != null)
        {
            // 自动生成的只给编辑器看：动画就叫 default（AnimatedSprite2D 的默认值），这样不用改 animation 属性，保存时也没有要还原的
            generated.RenameAnimation(preview, "default");
            shown = "default";
        }
        PreviewFrames = generated;
        SpriteFrames = frames;
        if (Animation != shown) Animation = shown;
        Frame = 0;
        EnemyAnimator.Layout(this, height, canvas);
        FlipH = PreviewFlip();
        PreviewAction = preview;
        PreviewShown = true;
    }

    /// <summary>还原成显示预览之前的样子（自动生成的帧拿掉，手动帧保持原样）。</summary>
    public void HidePreview()
    {
        if (!_hasSnapshot) return;
        _hasSnapshot = false;
        PreviewShown = false;
        SpriteFrames = _frames;
        if (Animation != _animation && _frames != null && _frames.HasAnimation(_animation)) Animation = _animation;
        if (_frames != null) Frame = _frame;
        Centered = _centered;
        Scale = _scale;
        Offset = _offset;
        Position = _position;
        FlipH = _flipH;
        PreviewFrames = null;
        PreviewAction = "";
    }
}
