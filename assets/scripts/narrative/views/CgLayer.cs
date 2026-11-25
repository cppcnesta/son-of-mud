using Godot;
using SonOfMud.Narrative.Data;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// CG 层：铺满全屏，盖在世界上面、对话框下面。界面在 cg_layer.tscn：Placeholder（色块）/ Image（CG 图）/ Caption（没有图时的标题）。有正式图时显示图；没有时铺 PlaceholderColor 色块，中间写标题（技术占位）。
/// 渐变用 Tween（暂停时照常进行）。
/// </summary>
[GlobalClass]
public partial class CgLayer : Control
{
    public const string ScenePath = "res://assets/scenes/narrative/views/cg_layer.tscn";

    /// <summary>当前（或正在淡入的）CG 编号；没有 CG 时为空。</summary>
    public string CurrentId { get; private set; } = "";
    public bool IsShowing => CurrentId != "";
    public string CurrentCaption => _caption?.Text ?? "";

    private ColorRect _placeholder;
    private TextureRect _image;
    private Label _caption;
    private Tween _tween;

    public override void _Ready()
    {
        _placeholder = ViewNodes.Need<ColorRect>(this, "Placeholder", ScenePath);
        _image = ViewNodes.Need<TextureRect>(this, "Image", ScenePath);
        _caption = ViewNodes.Need<Label>(this, "Caption", ScenePath);
        Modulate = new Color(1, 1, 1, 0);
        Visible = false;
    }

    public void Show(CgData data, float seconds)
    {
        if (data == null) return;
        FreeAnimated();
        CurrentId = data.Id;
        _image.Texture = data.Texture;
        _image.Visible = data.Texture != null;
        _placeholder.Color = data.PlaceholderColor;
        _caption.Text = data.Texture == null ? data.Title : "";
        Visible = true;
        FadeTo(1f, seconds);
    }

    public void Hide(float seconds)
    {
        if (!IsShowing && !Visible) return;
        CurrentId = "";
        FadeTo(0f, seconds);
    }

    // ---------- 动画 CG ----------

    /// <summary>现在放着的动画 CG 实例（没有是 null）。</summary>
    public AnimatedCg Animated { get; private set; }

    /// <summary>放出这个 CG 的动画场景（盖在静态图上面），淡入 CG 层。场景根节点不是 AnimatedCg 时返回 null。</summary>
    public AnimatedCg ShowAnimated(CgData data, float seconds)
    {
        if (data?.AnimatedScene == null) return null;
        FreeAnimated();
        Node node = data.AnimatedScene.Instantiate();
        if (node is not AnimatedCg cg)
        {
            node.QueueFree();
            return null;
        }
        cg.Name = "Animated";
        AddChild(cg);
        Animated = cg;
        CurrentId = data.Id;
        _image.Visible = false;
        _caption.Text = "";
        _placeholder.Color = Colors.Black;
        Visible = true;
        FadeTo(1f, seconds);
        return cg;
    }

    /// <summary>马上拿掉这个动画 CG 实例（取消播放时用）；它不是现在放着的那个就不动 CG 层。</summary>
    public void RemoveAnimated(AnimatedCg cg)
    {
        if (cg == null || !IsInstanceValid(cg)) return;
        bool current = cg == Animated;
        cg.QueueFree();
        if (!current) return;
        Animated = null;
        CurrentId = "";
        FadeTo(0f, 0f);
    }

    private void FreeAnimated()
    {
        if (Animated != null && IsInstanceValid(Animated)) Animated.QueueFree();
        Animated = null;
    }

    private void FadeTo(float alpha, float seconds)
    {
        _tween?.Kill();
        _tween = null;
        if (seconds <= 0f)
        {
            Modulate = new Color(1, 1, 1, alpha);
            Visible = alpha > 0f;
            if (alpha <= 0f && !IsShowing) FreeAnimated();
            return;
        }
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.TweenProperty(this, "modulate:a", alpha, seconds);
        if (alpha <= 0f) _tween.TweenCallback(Callable.From(() => { Visible = false; if (!IsShowing) FreeAnimated(); }));
    }
}
