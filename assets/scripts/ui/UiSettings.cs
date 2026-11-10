using Godot;
using GDict = Godot.Collections.Dictionary<string, string>;

namespace SonOfMud.UI;

/// <summary>UI 的可调数值和文案，放在 assets/data/ui/ui_settings.tres。</summary>
[GlobalClass]
public partial class UiSettings : Resource
{
    public const string DefaultPath = "res://assets/data/ui/ui_settings.tres";

    // ---- 泥珠血条（D10：每颗 20 HP） ----
    [Export] public int BeadHp { get; set; } = 20;
    [Export] public Vector2 BeadSize { get; set; } = new(32, 32);
    [Export] public int BeadSpacing { get; set; } = 4;
    /// <summary>扣血 / 回血时珠子液面变化的时长。</summary>
    [Export] public float BeadDrainSeconds { get; set; } = 0.25f;
    /// <summary>珠子掉空时碎裂效果的时长。</summary>
    [Export] public float BeadBreakSeconds { get; set; } = 0.3f;
    /// <summary>碎裂贴图 tex_ui_bead_break 横排的帧数。</summary>
    [Export] public int BeadBreakFrames { get; set; } = 6;

    // ---- 器物环（D6：只做显示） ----
    /// <summary>还没收到 ArtifactsChanged 时显示的状态（bit i = 第 i 件戴着）。</summary>
    [Export] public int InitialArtifactMask { get; set; } = 127;

    // ---- Boss 血条 ----
    [Export] public float BossBarDrainSeconds { get; set; } = 0.15f;
    /// <summary>延迟条（fill_delay）在主血条扣完之后多久才开始追上。</summary>
    [Export] public float BossBarDelaySeconds { get; set; } = 0.4f;
    [Export] public float BossBarDelayDrainSeconds { get; set; } = 0.5f;
    [Export] public float BossBarFadeSeconds { get; set; } = 0.3f;
    /// <summary>换阶段后的名字：键是 "bossId:阶段"，找不到时用 BossPhaseNameFormat（{0} 原名，{1} 阶段）。</summary>
    [Export] public GDict BossPhaseNames { get; set; } = new();
    [Export] public string BossPhaseNameFormat { get; set; } = "{0}（第{1}阶段）";

    // ---- 交互提示、提示条 ----
    [Export] public string InteractKeyLabel { get; set; } = "E";
    /// <summary>{0} 按键，{1} 提示文字。</summary>
    [Export] public string PromptFormat { get; set; } = "{0} {1}";
    [Export] public float ToastSeconds { get; set; } = 2f;
    [Export] public float ToastFadeSeconds { get; set; } = 0.3f;
    [Export] public string SavedText { get; set; } = "已存档";
    /// <summary>{0} 泥板名字（TabletNames 里找不到时用编号）。</summary>
    [Export] public string TabletFormat { get; set; } = "获得泥板「{0}」";
    [Export] public GDict TabletNames { get; set; } = new();

    // ---- HUD 整体 ----
    [Export] public float HudFadeSeconds { get; set; } = 0.3f;
    /// <summary>剧情进行中 HUD 的不透明度。</summary>
    [Export] public float HudNarrativeAlpha { get; set; } = 0f;

    // ---- 大地图 ----
    /// <summary>按住方向键时每秒平移的格数（按下的那一刻先移一格）。</summary>
    [Export] public float MapPanCellsPerSecond { get; set; } = 10f;

    // ---- 主菜单 ----
    [Export(PropertyHint.File, "*.tscn")] public string GameScene { get; set; } = "res://assets/scenes/world/main_game.tscn";
    /// <summary>调试版本里“旧白模”入口打开的旧主菜单。</summary>
    [Export(PropertyHint.File, "*.tscn")] public string LegacyMenuScene { get; set; } = "res://assets/scenes/ui/main_menu.tscn";
    [Export] public int SaveSlot { get; set; } = 0;
    [Export] public string StartErrorTitle { get; set; } = "无法开始旅程";

    public string BossPhaseName(string bossId, int phase, string baseName) =>
        BossPhaseNames != null && BossPhaseNames.TryGetValue($"{bossId}:{phase}", out string name) && !string.IsNullOrEmpty(name)
            ? name
            : string.Format(BossPhaseNameFormat, baseName, phase);

    public string TabletName(string tabletId) =>
        TabletNames != null && TabletNames.TryGetValue(tabletId, out string name) && !string.IsNullOrEmpty(name) ? name : tabletId;

    public static UiSettings LoadDefault() =>
        ResourceLoader.Exists(DefaultPath) ? GD.Load<UiSettings>(DefaultPath) : new UiSettings();
}
