using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 游戏音效库：按《NZZ音效对照表》的文件名自动分组，代码里只写组名。
/// - 文件放在 assets/audio/sfx/ 下（可以按类别分子文件夹：player/、minion/、boss01/……，子文件夹也会找），
///   名字是 SFX_&lt;组名&gt;_&lt;序号&gt;.wav（或 .ogg / .mp3），例如 SFX_Player_Jump_01.wav 属于 Player_Jump 组。
///   以后加 _04、_05 或新的一组，放进文件夹就生效。导出后的游戏里文件夹列出来的是 .import / .remap，同样认。
/// - Play(组名)：从这一组里随机挑一个（和这一组上一次挑的不一样；只有 1 个就播它），只把挑中的这一个交给
///   Events.SoundRequested（打击感服务再随机浮动音高）。这一组还没有文件时什么都不做、不报错。
/// </summary>
public static class SoundBank
{
    public const string DefaultDirectory = "res://assets/audio/sfx";

    // ---------- 组名（对照表里的文件命名去掉 SFX_ 和序号） ----------
    public const string MinionHit = "Minion_Hit";
    public const string MinionDeath = "Minion_Death";
    public const string BossSweep = "Boss01_Skill_Sweep";
    public const string BossTailStab = "Boss01_Skill_TailStab";
    public const string BossDeath = "Boss01_Death";
    public const string PlayerFootstep = "Player_Footstep";
    public const string PlayerJump = "Player_Jump";
    public const string PlayerDodge = "Player_Dodge";
    public const string PlayerDeath = "Player_Death";
    public const string PlayerRangedAttack = "Player_RangedAttack";
    public const string PlayerDownSlash = "Player_Attack_DownSlash";
    public const string PlayerBlock = "Player_Block";
    public const string PlayerHit = "Player_Hit";

    private static readonly Regex FilePattern = new(@"^SFX_(.+)_(\d+)\.(wav|ogg|mp3)$", RegexOptions.Compiled);
    private static readonly string[] NoFiles = Array.Empty<string>();

    private static string _directory = DefaultDirectory;
    private static Dictionary<string, string[]> _groups;
    private static readonly Dictionary<string, int> LastPick = new();
    private static readonly RandomNumberGenerator Rng = new();

    /// <summary>每播一次通知一次：组名、挑中的音频（测试和调试用）。</summary>
    public static event Action<string, AudioStream> Played;

    /// <summary>现在用的音效文件夹。</summary>
    public static string Directory => _directory;

    /// <summary>换音效文件夹（测试用），清掉分组和「上一次挑的」。传 null 回到默认文件夹。</summary>
    public static void UseDirectory(string directory)
    {
        _directory = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory.TrimEnd('/');
        Reload();
    }

    /// <summary>下次用到时重新列文件夹。</summary>
    public static void Reload()
    {
        _groups = null;
        LastPick.Clear();
    }

    /// <summary>
    /// 把文件夹里的文件（相对音效文件夹的路径，可以带子文件夹和 .import / .remap）按组名分组，每组按文件名排序。
    /// 只看文件名本身是否符合 SFX_&lt;组名&gt;_&lt;序号&gt;，不符合的不要。
    /// </summary>
    public static Dictionary<string, string[]> Group(IEnumerable<string> listing)
    {
        var lists = new Dictionary<string, List<string>>();
        foreach (string name in SpriteStrip.SourceFileNames(listing))
        {
            Match match = FilePattern.Match(name[(name.LastIndexOf('/') + 1)..]);
            if (!match.Success) continue;
            string group = match.Groups[1].Value;
            if (!lists.TryGetValue(group, out List<string> files)) lists[group] = files = new List<string>();
            files.Add(name);
        }
        var result = new Dictionary<string, string[]>();
        foreach (var (group, files) in lists)
        {
            files.Sort((a, b) => string.CompareOrdinal(a[(a.LastIndexOf('/') + 1)..], b[(b.LastIndexOf('/') + 1)..]));
            result[group] = files.ToArray();
        }
        return result;
    }

    private static Dictionary<string, string[]> Groups
    {
        get
        {
            if (_groups != null) return _groups;
            _groups = Group(ListFiles(_directory));
            return _groups;
        }
    }

    /// <summary>文件夹和所有子文件夹里的文件，路径相对这个文件夹（如 player/SFX_Player_Jump_01.wav.import）；文件夹不存在时是空的。</summary>
    public static List<string> ListFiles(string directory)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(directory) || !DirAccess.DirExistsAbsolute(directory)) return result;
        Collect(directory.TrimEnd('/'), "", result);
        return result;
    }

    private static void Collect(string root, string prefix, List<string> result)
    {
        string dir = prefix == "" ? root : $"{root}/{prefix.TrimEnd('/')}";
        foreach (string file in DirAccess.GetFilesAt(dir)) result.Add(prefix + file);
        foreach (string sub in DirAccess.GetDirectoriesAt(dir)) Collect(root, $"{prefix}{sub}/", result);
    }

    /// <summary>这一组的所有文件（res:// 路径，按文件名排序）；没有时是空的。</summary>
    public static IReadOnlyList<string> Variants(string group)
    {
        if (string.IsNullOrEmpty(group) || !Groups.TryGetValue(group, out string[] files)) return NoFiles;
        var paths = new string[files.Length];
        for (int i = 0; i < files.Length; i++) paths[i] = $"{_directory}/{files[i]}";
        return paths;
    }

    /// <summary>这一组有没有文件。</summary>
    public static bool Has(string group) => !string.IsNullOrEmpty(group) && Groups.ContainsKey(group);

    /// <summary>从这一组里随机挑一个，和这一组上一次挑的不一样（只有 1 个就是它）；没有文件或载入失败返回 null。</summary>
    public static AudioStream Pick(string group)
    {
        IReadOnlyList<string> paths = Variants(group);
        if (paths.Count == 0) return null;
        int last = LastPick.TryGetValue(group, out int l) ? l : -1;
        int index;
        if (paths.Count == 1) index = 0;
        else
        {
            // 从「除了上一次」的里面挑
            index = Rng.RandiRange(0, last >= 0 && last < paths.Count ? paths.Count - 2 : paths.Count - 1);
            if (last >= 0 && last < paths.Count && index >= last) index++;
        }
        LastPick[group] = index;
        return ResourceLoader.Exists(paths[index]) ? GD.Load<AudioStream>(paths[index]) : null;
    }

    /// <summary>播这一组里随机的一个。这一组没有文件时什么都不做，返回 false。</summary>
    public static bool Play(string group)
    {
        AudioStream stream = Pick(group);
        if (stream == null) return false;
        Events.Instance?.EmitSignal(Events.SignalName.SoundRequested, new Godot.Collections.Array { stream });
        Played?.Invoke(group, stream);
        return true;
    }

    /// <summary>按顺序播第一个有文件的组（例如「死亡声，没有就用受击声」），每次只播一个。都没有返回 false。</summary>
    public static bool PlayFirst(params string[] groups)
    {
        foreach (string group in groups)
            if (Has(group)) return Play(group);
        return false;
    }
}
