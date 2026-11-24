using System;
using System.Collections.Generic;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 选项面板。界面在 choice_panel.tscn：List（排列选项的容器）、OptionTemplate（选项按钮模板，隐藏；每个选项复制一份，样式以它为准）。
/// 屏幕中间一列按钮。键盘由剧情层读（上下移动、推进键确认），鼠标悬停改选、点击确认。
/// 每个选项的外观：Fill（半透明底，颜色在模板的样式里调）、FrameNormal / FrameFocus（没选中 / 选中的框）、Arrow（选中时的箭头）。
/// 选中后先关面板再回调，回调只会发生一次。
/// </summary>
[GlobalClass]
public partial class ChoicePanel : Control
{
    public const string ScenePath = "res://assets/scenes/narrative/views/choice_panel.tscn";

    /// <summary>选中 / 没选中的选项文字颜色（在 choice_panel.tscn 根节点上调）。</summary>
    [Export] public Color FocusedTextColor { get; set; } = new(1f, 0.94f, 0.78f);
    [Export] public Color NormalTextColor { get; set; } = new(0.84f, 0.8f, 0.72f);

    public bool IsOpen { get; private set; }
    public int Focus { get; private set; }
    public IReadOnlyList<string> Options => _options;

    private readonly List<string> _options = new();
    private readonly List<Button> _buttons = new();
    private Container _list;
    private Button _template;
    private Action<int> _onChosen;

    public override void _Ready()
    {
        _list = ViewNodes.Need<Container>(this, "List", ScenePath);
        _template = ViewNodes.Need<Button>(this, "OptionTemplate", ScenePath);
        if (_template != null) _template.Visible = false;
        Visible = false;
    }

    public void Open(string[] options, Action<int> onChosen)
    {
        Close();
        _onChosen = onChosen;
        _options.AddRange(options);
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var button = (Button)_template.Duplicate();
            button.Name = "Option" + i;
            button.Text = options[i];
            button.Visible = true;
            button.Pressed += () => Choose(index);
            button.MouseEntered += () => SetFocus(index);
            _list.AddChild(button);
            _buttons.Add(button);
        }
        IsOpen = true;
        Visible = true;
        SetFocus(0);
    }

    public void Move(int step)
    {
        if (!IsOpen || _buttons.Count == 0) return;
        SetFocus(Mathf.PosMod(Focus + step, _buttons.Count));
    }

    public void ConfirmFocused() => Choose(Focus);

    public void Choose(int index)
    {
        if (!IsOpen || index < 0 || index >= _options.Count) return;
        Action<int> callback = _onChosen;
        Close();
        callback?.Invoke(index);
    }

    public void Close()
    {
        IsOpen = false;
        _onChosen = null;
        _options.Clear();
        foreach (Button button in _buttons) button.QueueFree();
        _buttons.Clear();
        Focus = 0;
        Visible = false;
    }

    /// <summary>选中的选项亮起：换成 FrameFocus 框、显示 Arrow 箭头、字变亮；其余用 FrameNormal 框（三个子节点在模板里，跟着复制）。</summary>
    private void SetFocus(int index)
    {
        Focus = index;
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == index;
            Button button = _buttons[i];
            if (button.GetNodeOrNull<CanvasItem>("FrameFocus") is { } focus) focus.Visible = on;
            if (button.GetNodeOrNull<CanvasItem>("FrameNormal") is { } normal) normal.Visible = !on;
            if (button.GetNodeOrNull<CanvasItem>("Arrow") is { } arrow) arrow.Visible = on;
            Color text = on ? FocusedTextColor : NormalTextColor;
            foreach (StringName name in TextColors) button.AddThemeColorOverride(name, text);
        }
    }

    private static readonly StringName[] TextColors = { "font_color", "font_hover_color", "font_focus_color" };
}
