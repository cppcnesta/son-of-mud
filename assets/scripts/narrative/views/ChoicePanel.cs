using System;
using System.Collections.Generic;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 选项面板（技术占位版）：屏幕中间一列按钮。键盘由剧情层读（上下移动、推进键确认），鼠标悬停改选、点击确认。
/// 选中后先关面板再回调，回调只会发生一次。
/// </summary>
[GlobalClass]
public partial class ChoicePanel : Control
{
    public bool IsOpen { get; private set; }
    public int Focus { get; private set; }
    public IReadOnlyList<string> Options => _options;

    private readonly List<string> _options = new();
    private readonly List<Button> _buttons = new();
    private VBoxContainer _list;
    private Action<int> _onChosen;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // 已在场景树里：只改锚点会保留原来 0 大小的矩形，必须连偏移一起设
        MouseFilter = MouseFilterEnum.Ignore;
        _list = new VBoxContainer { Name = "List", MouseFilter = MouseFilterEnum.Ignore };
        _list.SetAnchorsPreset(LayoutPreset.Center);
        _list.OffsetLeft = -180;
        _list.OffsetRight = 180;
        _list.OffsetTop = -120;
        _list.OffsetBottom = 40;
        _list.AddThemeConstantOverride("separation", 12);
        AddChild(_list);
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
            var button = new Button { Name = "Option" + i, Text = options[i], FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(360, 52) };
            button.AddThemeFontSizeOverride("font_size", 22);
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

    private void SetFocus(int index)
    {
        Focus = index;
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == index;
            _buttons[i].Text = (on ? "▶ " : "") + _options[i];
            _buttons[i].Modulate = on ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
        }
    }
}
