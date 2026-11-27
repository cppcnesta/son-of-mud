using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Data;

namespace SonOfMud.UI;

/// <summary>
/// 主页面的「收集」面板（title_menu.tscn 的 CollectionPanel）。两个页签，内容按跨存档记录 CollectionRecord 显示：
/// - 泥板：按地图顺序列出全部泥板（TabletIds）。捡到过的显示正文：没通关是残缺版，通关后有完整版的显示完整版；
///   没捡到过的照样列出标题，正文只写 NotCollectedText。
/// - CG：按 CG1～CG8 列出（CgIds）。没解锁写 LockedText；解锁了显示标题和图（有静态图显示图，动画 CG 和没交图的显示占位色块 + 标题）。
/// 操作：上下键 / 鼠标选条目，左右键或点页签换页。开关、遮罩、Esc 由 TitleMenu 管。
/// </summary>
public partial class TitleCollectionPanel : Control
{
    public enum Page { Tablets, Cgs }

    public const string NotCollectedText = "还没有捡到。";
    public const string LockedText = "未解锁";

    [Export] public BaseButton TabletTab { get; set; }
    [Export] public BaseButton CgTab { get; set; }
    [Export] public ItemList List { get; set; }
    [Export] public Label EntryName { get; set; }
    [Export] public Label EntryBody { get; set; }
    [Export] public Control CgView { get; set; }
    [Export] public TextureRect CgImage { get; set; }
    [Export] public ColorRect CgPlaceholder { get; set; }
    [Export] public Label CgPlaceholderLabel { get; set; }

    /// <summary>泥板按地图顺序（地图一 → 地图四，最后是结局里的「名字」「星」）。</summary>
    [Export] public string[] TabletIds { get; set; } =
    {
        "nameless_day", "divine_son", "smile", "after_flood", "destiny", "hoped_world", "too_soft", "curse", "annals",
        "gate_1", "gate_2", "gate_3", "gate_4", "gate_5", "gate_6", "gate_7", "name", "star",
    };
    /// <summary>CG1～CG8（纯黑屏不算）。</summary>
    [Export] public string[] CgIds { get; set; } =
    {
        "seven_nights", "reshape", "sea_war", "meteor", "clay", "patricide", "third_world", "slumber",
    };
    [Export] public Color ActiveTabColor { get; set; } = new(0.96f, 0.86f, 0.6f);
    [Export] public Color InactiveTabColor { get; set; } = new(0.6f, 0.56f, 0.5f);
    [Export] public Color LockedItemColor { get; set; } = new(0.55f, 0.52f, 0.48f);

    public Page CurrentPage { get; private set; } = Page.Tablets;
    public int SelectedIndex { get; private set; } = -1;
    /// <summary>现在这一页每个条目在列表里显示的字（测试用）。</summary>
    public IReadOnlyList<string> ItemTexts => Enumerable.Range(0, List?.ItemCount ?? 0).Select(i => List.GetItemText(i)).ToList();

    private NarrativeSettings _narrative;

    public override void _Ready()
    {
        _narrative = GD.Load<NarrativeSettings>(NarrativeSettings.DefaultPath) ?? new NarrativeSettings();
        if (TabletTab != null) TabletTab.Pressed += () => ShowPage(Page.Tablets);
        if (CgTab != null) CgTab.Pressed += () => ShowPage(Page.Cgs);
        if (List != null) List.ItemSelected += index => Select((int)index);
    }

    /// <summary>每次打开面板时调用：按最新记录重排，回到泥板页第一项。</summary>
    public void Refresh() => ShowPage(Page.Tablets);

    public void ShowPage(Page page)
    {
        CurrentPage = page;
        if (TabletTab != null) TabletTab.Modulate = page == Page.Tablets ? ActiveTabColor : InactiveTabColor;
        if (CgTab != null) CgTab.Modulate = page == Page.Cgs ? ActiveTabColor : InactiveTabColor;
        if (List == null) return;
        List.Clear();
        string[] ids = page == Page.Tablets ? TabletIds : CgIds;
        foreach (string id in ids)
        {
            bool open = page == Page.Tablets ? CollectionRecord.HasTablet(id) : CollectionRecord.HasCg(id);
            int index = List.AddItem(page == Page.Tablets ? TabletTitle(id) : CgListText(id, open));
            if (!open) List.SetItemCustomFgColor(index, LockedItemColor);
        }
        if (List.ItemCount > 0)
        {
            List.Select(0);
            List.EnsureCurrentIsVisible();
        }
        Select(List.ItemCount > 0 ? 0 : -1);
    }

    public void Select(int index)
    {
        SelectedIndex = index;
        if (List != null && index >= 0 && index < List.ItemCount && !List.IsSelected(index)) List.Select(index);
        if (CurrentPage == Page.Tablets) ShowTablet(index >= 0 && index < TabletIds.Length ? TabletIds[index] : null);
        else ShowCg(index >= 0 && index < CgIds.Length ? CgIds[index] : null);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event.IsEcho()) return;
        if (@event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_right"))
        {
            ShowPage(CurrentPage == Page.Tablets ? Page.Cgs : Page.Tablets);
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------- 泥板 ----------

    private TabletData LoadTablet(string id) =>
        id != null && ResourceLoader.Exists(_narrative.TabletPath(id)) ? GD.Load<TabletData>(_narrative.TabletPath(id)) : null;

    private string TabletTitle(string id) => LoadTablet(id)?.Title is { Length: > 0 } title ? title : id;

    private void ShowTablet(string id)
    {
        if (CgView != null) CgView.Visible = false;
        if (EntryBody != null) EntryBody.Visible = true;
        TabletData data = LoadTablet(id);
        ShowEntryName(data?.Title ?? "");
        if (EntryBody == null) return;
        if (data == null) EntryBody.Text = "";
        else if (!CollectionRecord.HasTablet(id)) EntryBody.Text = NotCollectedText;
        else EntryBody.Text = data.TextFor(CollectionRecord.Cleared);
    }

    // ---------- CG ----------

    private CgData LoadCg(string id) =>
        id != null && ResourceLoader.Exists(_narrative.CgPath(id)) ? GD.Load<CgData>(_narrative.CgPath(id)) : null;

    /// <summary>CG 标题里「」前面的编号，比如 CG1「七夜」→ CG1。</summary>
    private static string CgNumber(CgData data)
    {
        string title = data?.Title ?? "";
        int cut = title.IndexOf('「');
        return cut > 0 ? title[..cut] : title;
    }

    private string CgListText(string id, bool open)
    {
        CgData data = LoadCg(id);
        if (open) return data?.Title is { Length: > 0 } title ? title : id;
        string number = CgNumber(data);
        return string.IsNullOrEmpty(number) ? LockedText : $"{number}　{LockedText}";
    }

    private void ShowCg(string id)
    {
        if (EntryBody != null) EntryBody.Visible = false;
        CgData data = LoadCg(id);
        bool open = id != null && CollectionRecord.HasCg(id);
        if (!open)
        {
            ShowEntryName(LockedText);
            if (CgView != null) CgView.Visible = false;
            return;
        }
        ShowEntryName(data?.Title ?? id);
        if (CgView != null) CgView.Visible = true;
        bool hasImage = data?.Texture != null;
        if (CgImage != null)
        {
            CgImage.Texture = data?.Texture;
            CgImage.Visible = hasImage;
        }
        if (CgPlaceholder != null)
        {
            CgPlaceholder.Visible = !hasImage;
            if (data != null) CgPlaceholder.Color = data.PlaceholderColor;
        }
        if (CgPlaceholderLabel != null) CgPlaceholderLabel.Text = data?.Title ?? "";
    }

    private void ShowEntryName(string text)
    {
        if (EntryName != null) EntryName.Text = text;
    }

    /// <summary>现在显示的 CG 是不是占位色块（测试用）。</summary>
    public bool ShowingCgPlaceholder => CgView != null && CgView.Visible && CgPlaceholder != null && CgPlaceholder.Visible;
    public bool ShowingCgImage => CgView != null && CgView.Visible && CgImage != null && CgImage.Visible && CgImage.Texture != null;
}
