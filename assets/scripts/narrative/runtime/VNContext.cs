using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Views;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 命令执行时能用到的一切：执行器、各个表现层、数据、变量、正在进行的玩法。命令只通过它和外界打交道。
/// 表现层可以为空（无界面的测试）：命令遇到空的表现层就直接完成。
/// 对话框执行器和字幕执行器各有一份，共用变量和数据。
/// </summary>
public sealed class VNContext
{
    // ---------- 执行器和表现层 ----------
    public VNRunner Runner;
    public RunnerMode Mode = RunnerMode.Dialog;
    public ILineSurface Lines;
    public ChoicePanel Choice;
    public TabletView Tablets;
    public CgLayer Cg;
    public FadeLayer Fade;
    public NarrativeAudio Audio;

    // ---------- 数据 ----------
    public NarrativeSettings Settings = new();
    public CharacterDatabase Characters;
    public IStoryVariables Vars = new CoreStoryVariables();

    /// <summary>[表情] 记下的每个角色当前表情（键是角色的显示名）。</summary>
    public readonly Dictionary<string, string> Expressions = new();

    /// <summary>[重开] 设为 true；剧情层在这段剧情结束后发 RestartRunRequested。</summary>
    public bool RestartRequested;

    public float LineGap => Settings?.LineGap ?? 0.5f;
    public bool PauseAtBlankLines => Settings?.PauseAtBlankLines ?? true;

    // ---------- 玩法段 ----------
    public IGameplaySegment ActiveSegment;
    public bool SegmentHoldsInputLock;

    // ---------- 场次 ----------
    // 每次重新播放（或停止），场次 +1。命令里“过一会儿才回来”的回调先记下场次，回来时场次变了就忽略。
    public int Session { get; private set; }

    public bool IsCurrent(int session) => session == Session;

    /// <summary>开始新的一场：取消玩法、清掉剧情输入锁、收起这个执行器用的界面。变量保留。</summary>
    public void BeginSession()
    {
        Session++;
        if (ActiveSegment != null)
        {
            try { ActiveSegment.Cancel(); }
            catch (Exception e) { NarrativeLog.Error("[剧情] 取消玩法时出错：" + e.Message); }
        }
        ActiveSegment = null;
        SegmentHoldsInputLock = false;
        _inputLocks = 0;
        RestartRequested = false;
        Fade?.Cancel();
        Choice?.Close();
        Tablets?.Close();
        Lines?.Hide();
    }

    // ---------- 剧情内部的输入锁（转场、CG 单独展示、玩法期间不接受推进键）----------
    private int _inputLocks;

    public bool InputLocked => _inputLocks > 0;
    public void LockInput() => _inputLocks++;
    public void UnlockInput() => _inputLocks = Math.Max(0, _inputLocks - 1);

    public void ReleaseSegmentInputLock()
    {
        if (!SegmentHoldsInputLock) return;
        SegmentHoldsInputLock = false;
        UnlockInput();
    }

    // ---------- 名字 ----------

    /// <summary>剧本里的名字对应的“规范名”：登记过的角色用显示名，否则原样。改名和表情都按它记。</summary>
    public string CanonicalName(string speaker) => Characters?.Find(speaker)?.DisplayName ?? speaker ?? "";

    /// <summary>名牌上显示的名字：旁白为空；改过名（变量 name.&lt;规范名&gt;）用新名字；否则用显示名。</summary>
    public string DisplayNameFor(string speaker)
    {
        if (Characters != null && Characters.IsHiddenName(speaker)) return "";
        if (Characters == null && (string.IsNullOrEmpty(speaker) || speaker == "旁白")) return "";
        string key = "name." + CanonicalName(speaker);
        if (Vars != null && Vars.Has(key))
        {
            Variant renamed = Vars.Get(key);
            if (renamed.VariantType == Variant.Type.String && renamed.AsString() != "") return renamed.AsString();
        }
        return CanonicalName(speaker);
    }

    // ---------- 数据加载 ----------

    public TabletData LoadTablet(string id) => LoadResource<TabletData>(Settings.TabletPath(id));
    public TabletData LoadInscription(string id) => LoadResource<TabletData>(Settings.InscriptionPath(id));
    public CgData LoadCg(string id) => LoadResource<CgData>(Settings.CgPath(id));

    private static T LoadResource<T>(string path) where T : Resource =>
        ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;

    /// <summary>字幕条上一句停留多久（打完字之后）。</summary>
    public float SubtitleHold(string text) =>
        (Settings?.SubtitleBaseSeconds ?? 1.2f) + (Settings?.SubtitleSecondsPerChar ?? 0.12f) * (text ?? "").Length;
}
