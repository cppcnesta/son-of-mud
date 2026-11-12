using Godot;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 对话框（技术占位版）：屏幕下方的底板、名牌、立绘占位、逐字显示的正文（RichTextLabel，支持 BBCode）。
/// 同一个人连着说的几句攒在同一个框里：ShowLine 开始新的一段，AppendLine 在下面加一句；加上这一句放不下时翻页，从这一句开始新的一页。
/// 不读输入：推进键由剧情层统一读，再交给执行器。界面在 _Ready 里用代码搭好，美术交付后可以换成场景。
/// </summary>
[GlobalClass]
public partial class DialogBox : Control, ILineSurface
{
    [Export] public NarrativeSettings Settings { get; set; }

    public bool IsTyping { get; private set; }
    public bool Rushing { get; private set; }
    /// <summary>对话框现在显示着。</summary>
    public bool IsOpen => _box != null && _box.Visible;
    public string CurrentName => _name?.Text ?? "";
    public bool NameVisible => _name != null && _name.Visible;
    public string CurrentText => _body?.Text ?? "";
    /// <summary>正文里已经显示出来的字数（-1 表示全部）。</summary>
    public int VisibleCharacters => _body?.VisibleCharacters ?? 0;
    public bool PortraitVisible => _portrait != null && _portrait.Visible;
    public string PortraitCaption => _portraitLabel?.Text ?? "";
    /// <summary>每出现一句（ShowLine 或 AppendLine）加一。</summary>
    public int LineCount { get; private set; }
    /// <summary>同一个人连着说、框里放不下而翻页的次数。</summary>
    public int PageCount { get; private set; }
    /// <summary>正文现在的内容完整放得下（排版后的高度不超过正文区）。</summary>
    public bool TextFits => _body == null || _body.GetContentHeight() <= _body.Size.Y + 0.5f;

    private Panel _box;
    private Label _name;
    private RichTextLabel _body;
    private Label _hint;
    private ColorRect _portrait;
    private TextureRect _portraitImage;
    private Label _portraitLabel;
    private string _blockSpeaker;   // 这一段是谁在说；null 表示没有可以接着往下加的内容
    private bool _paragraphPending;
    private double _shown;

    public override void _Ready()
    {
        Settings ??= new NarrativeSettings();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // 已在场景树里：只改锚点会保留原来 0 大小的矩形，必须连偏移一起设
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
        Hide();
    }

    private void Build()
    {
        _box = new Panel { Name = "Box", MouseFilter = MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(LayoutPreset.BottomWide);
        _box.OffsetLeft = 56;
        _box.OffsetRight = -56;
        _box.OffsetTop = -204;
        _box.OffsetBottom = -24;
        _box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.05f, 0.04f, 0.88f), BorderColor = new Color(0.55f, 0.45f, 0.3f), BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
        AddChild(_box);

        _portrait = new ColorRect { Name = "Portrait", Position = new Vector2(16, 16), Size = new Vector2(148, 148), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _box.AddChild(_portrait);
        _portraitImage = new TextureRect { Name = "Image", Size = new Vector2(148, 148), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore };
        _portrait.AddChild(_portraitImage);
        _portraitLabel = new Label { Name = "Caption", Size = new Vector2(148, 148), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _portrait.AddChild(_portraitLabel);

        _name = new Label { Name = "Name", Position = new Vector2(184, 10), Size = new Vector2(400, 30) };
        _name.AddThemeFontSizeOverride("font_size", 22);
        _box.AddChild(_name);

        _body = new RichTextLabel { Name = "Body", BbcodeEnabled = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, FitContent = false };
        _body.SetAnchorsPreset(LayoutPreset.FullRect);
        _body.OffsetLeft = 184;
        _body.OffsetTop = 46;
        _body.OffsetRight = -24;
        _body.OffsetBottom = -16;
        _body.AddThemeFontSizeOverride("normal_font_size", 22);
        _box.AddChild(_body);

        _hint = new Label { Name = "Hint", Text = "▼", Visible = false };
        _hint.SetAnchorsPreset(LayoutPreset.BottomRight);
        _hint.OffsetLeft = -40;
        _hint.OffsetTop = -36;
        _box.AddChild(_hint);
    }

    // ---------- ILineSurface ----------

    public void ShowLine(string name, string text, CharacterData character, string expression)
    {
        LineCount++;
        Show();
        _box.Visible = true;
        bool hasName = !string.IsNullOrEmpty(name);
        _name.Visible = hasName;
        _name.Text = hasName ? name : "";
        _name.Modulate = character?.NameColor ?? new Color(0.95f, 0.85f, 0.6f);
        ShowPortrait(character, expression);
        bool portrait = _portrait.Visible;
        _name.Position = new Vector2(portrait ? 184 : 24, 10);
        _body.OffsetLeft = portrait ? 184 : 24;

        _blockSpeaker = name ?? "";
        _paragraphPending = false;
        Rushing = false;
        _body.Text = text;
        StartTyping(0);
    }

    public bool CanAppend(string name) => _blockSpeaker != null && _blockSpeaker == (name ?? "") && IsOpen;

    public void AppendLine(string text)
    {
        LineCount++;
        CompleteTyping();
        int alreadyShown = _body.GetTotalCharacterCount();
        _body.Text += (_paragraphPending ? "\n\n" : "\n") + text;
        _paragraphPending = false;
        if (!TextFits)
        {
            // 加上这句就放不下了：翻页，新的一页从这一句开始（名牌、立绘不变）
            _body.Text = text;
            alreadyShown = 0;
            PageCount++;
        }
        if (Rushing) CompleteTyping();
        else StartTyping(alreadyShown);
    }

    public bool FitsAppend(string text)
    {
        if (_body == null) return true;
        string saved = _body.Text;
        int visible = _body.VisibleCharacters;
        _body.Text = saved + (_paragraphPending ? "\n\n" : "\n") + text;
        bool fits = TextFits;
        _body.Text = saved;
        _body.VisibleCharacters = visible;
        return fits;
    }

    public void BreakParagraph() => _paragraphPending = true;

    public void Rush()
    {
        Rushing = true;
        CompleteTyping();
    }

    public void StopRush() => Rushing = false;

    public void CompleteTyping()
    {
        IsTyping = false;
        if (_body != null) _body.VisibleCharacters = -1;
    }

    public new void Hide()
    {
        CompleteTyping();
        _blockSpeaker = null;
        _paragraphPending = false;
        Rushing = false;
        if (_box != null) _box.Visible = false;
        if (_hint != null) _hint.Visible = false;
    }

    // ---------- 打字 ----------

    private void StartTyping(int from)
    {
        IsTyping = true;
        _shown = from;
        _body.VisibleCharacters = from;
    }

    public override void _Process(double delta)
    {
        if (_hint != null) _hint.Visible = IsOpen && !IsTyping;
        if (!IsTyping) return;
        int total = _body.GetTotalCharacterCount();
        _shown += Settings.CharsPerSecond * delta;
        if (_shown >= total) CompleteTyping();
        else _body.VisibleCharacters = (int)_shown;
    }

    private void ShowPortrait(CharacterData character, string expression)
    {
        bool show = character != null && character.HasPortrait;
        _portrait.Visible = show;
        if (!show) return;
        _portraitImage.Texture = character.Portrait;
        _portrait.Color = character.Portrait != null ? new Color(0, 0, 0, 0) : character.PortraitColor;
        _portraitLabel.Visible = character.Portrait == null;
        _portraitLabel.Text = string.IsNullOrEmpty(expression) ? character.DisplayName : $"{character.DisplayName}\n（{expression}）";
    }
}
