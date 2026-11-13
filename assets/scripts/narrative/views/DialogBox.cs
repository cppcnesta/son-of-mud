using Godot;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 对话框。界面在 <see cref="ScenePath"/>（dialog_box.tscn）里，位置、大小、底图、字号都以场景为准，代码只填内容、控制显示。
/// 场景里的节点（名字固定，改名代码就找不到）：
///   Portrait（大立绘，在对话框上方）→ Placeholder（没有图时的色块）/ Image（立绘图）/ Caption（没有图时写名字）
///   Box（对话框底板）→ Name（名牌）/ Body（正文，逐字显示，支持 BBCode）/ Hint（▼ 等待推进）
/// 同一个人连着说的几句攒在同一个框里：ShowLine 开始新的一段，AppendLine 在下面加一句；加上这一句放不下时翻页，从这一句开始新的一页。
/// 一句话自己就放不下时，这一页的字号逐步缩小到放得下为止（最小 <see cref="MinFontSize"/>），换页后恢复场景里的字号。
/// 不读输入：推进键由剧情层统一读，再交给执行器。
/// </summary>
[GlobalClass]
public partial class DialogBox : Control, ILineSurface
{
    public const string ScenePath = "res://assets/scenes/narrative/views/dialog_box.tscn";
    public const int MinFontSize = 14;
    private const string FontSizeName = "normal_font_size";

    [Export] public NarrativeSettings Settings { get; set; }

    /// <summary>
    /// 有立绘时，名牌和正文整体往右挪多少像素（正文右边不动，所以变窄，换行和翻页按变窄后的宽度算）。
    /// 场景里 Name / Body 的位置是“没有立绘时”的位置；这里填 0 就是有没有立绘都一样。
    /// </summary>
    [Export] public float PortraitTextShift { get; set; }

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
    /// <summary>立绘现在用的图（没有图时为 null）。</summary>
    public Texture2D PortraitTexture => _portraitImage?.Texture;
    /// <summary>每出现一句（ShowLine 或 AppendLine）加一。</summary>
    public int LineCount { get; private set; }
    /// <summary>同一个人连着说、框里放不下而翻页的次数。</summary>
    public int PageCount { get; private set; }
    /// <summary>场景里正文的字号（这一页没被缩小时用它）。</summary>
    public int BaseFontSize { get; private set; }
    /// <summary>这一页正文现在的字号。</summary>
    public int CurrentFontSize => _body?.GetThemeFontSize(FontSizeName) ?? 0;
    /// <summary>正文现在的内容完整放得下（排版后的高度不超过正文区）。</summary>
    public bool TextFits => _body == null || _body.GetContentHeight() <= _body.Size.Y + 0.5f;

    private Control _box;
    private Label _name;
    private RichTextLabel _body;
    private Control _hint;
    private Control _portrait;
    private ColorRect _portraitPlaceholder;
    private TextureRect _portraitImage;
    private Label _portraitLabel;
    private string _blockSpeaker;   // 这一段是谁在说；null 表示没有可以接着往下加的内容
    private bool _paragraphPending;
    private double _shown;
    private float _nameLeft, _nameRight, _bodyLeft;   // 场景里（没有立绘时）的位置

    public override void _Ready()
    {
        Settings ??= new NarrativeSettings();
        _box = Need<Control>("Box");
        _name = Need<Label>("Box/Name");
        _body = Need<RichTextLabel>("Box/Body");
        _hint = Need<Control>("Box/Hint");
        _portrait = Need<Control>("Portrait");
        _portraitPlaceholder = Need<ColorRect>("Portrait/Placeholder");
        _portraitImage = Need<TextureRect>("Portrait/Image");
        _portraitLabel = Need<Label>("Portrait/Caption");
        BaseFontSize = _body?.GetThemeFontSize(FontSizeName) ?? 22;
        if (_name != null) (_nameLeft, _nameRight) = (_name.OffsetLeft, _name.OffsetRight);
        if (_body != null) _bodyLeft = _body.OffsetLeft;
        Hide();
    }

    private T Need<T>(string path) where T : Node
    {
        T node = GetNodeOrNull<T>(path);
        if (node == null) GD.PushError($"对话框场景里找不到 {path}（{typeof(T).Name}）。请检查 {ScenePath}，节点名不能改。");
        return node;
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
        ApplyTextShift(_portrait != null && _portrait.Visible);

        _blockSpeaker = name ?? "";
        _paragraphPending = false;
        Rushing = false;
        StartPage(text);
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
            StartPage(text);
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
        if (_portrait != null) _portrait.Visible = false;
    }

    // ---------- 分页和字号 ----------

    /// <summary>新的一页：恢复场景字号；这一句自己就放不下时逐步缩小字号，直到放得下或到最小字号。</summary>
    private void StartPage(string text)
    {
        _body.AddThemeFontSizeOverride(FontSizeName, BaseFontSize);
        _body.Text = text;
        int size = BaseFontSize;
        while (!TextFits && size > MinFontSize)
        {
            size = Mathf.Max(MinFontSize, size - 2);
            _body.AddThemeFontSizeOverride(FontSizeName, size);
        }
        if (!TextFits)
            GD.PushWarning($"对话框：这一句太长，缩到 {MinFontSize} 号字也放不下，请在剧本里拆成几句：{text}");
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

    /// <summary>有立绘时名牌和正文右移 PortraitTextShift；没有时回到场景里的位置。</summary>
    private void ApplyTextShift(bool portrait)
    {
        float shift = portrait ? PortraitTextShift : 0f;
        if (_name != null)
        {
            _name.OffsetLeft = _nameLeft + shift;
            _name.OffsetRight = _nameRight + shift;
        }
        if (_body != null) _body.OffsetLeft = _bodyLeft + shift;
    }

    /// <summary>立绘：角色勾了 HasPortrait 才显示；有图显示图（按表情找，找不到用默认），没图显示色块加名字。</summary>
    private void ShowPortrait(CharacterData character, string expression)
    {
        bool show = character != null && character.HasPortrait;
        _portrait.Visible = show;
        if (!show) return;
        Texture2D texture = character.PortraitFor(expression);
        _portraitImage.Texture = texture;
        _portraitImage.Visible = texture != null;
        _portraitPlaceholder.Visible = texture == null;
        _portraitPlaceholder.Color = character.PortraitColor;
        _portraitLabel.Visible = texture == null;
        _portraitLabel.Text = string.IsNullOrEmpty(expression) ? character.DisplayName : $"{character.DisplayName}\n（{expression}）";
    }
}
