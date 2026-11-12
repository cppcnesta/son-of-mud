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

    private void FadeTo(float alpha, float seconds)
    {
        _tween?.Kill();
        _tween = null;
        if (seconds <= 0f)
        {
            Modulate = new Color(1, 1, 1, alpha);
            Visible = alpha > 0f;
            return;
        }
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.TweenProperty(this, "modulate:a", alpha, seconds);
        if (alpha <= 0f) _tween.TweenCallback(Callable.From(() => Visible = false));
    }
}
