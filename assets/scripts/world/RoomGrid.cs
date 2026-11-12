using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间地形文字图（关卡几何计划 v3 第 3.1 节）：一个字符 = 一个 32 像素瓦片。
/// 文件在 <see cref="DirectoryPath"/> 下，一个房间一个，例如 S1_02.txt。生成工具、静态模拟器、测试都从这里读。
///
/// 文件格式：
/// <code>
/// # 注释（任意行，以 # 加空格开头；只允许出现在 --- 之前）
/// @room S1_05
/// @size 72x31              列 x 行，必须等于 ExpectedSize
/// @cond boss_keeper        R 的出现条件（剧情标记）；图里有 R 时必须写，没有 R 时不能写
/// @anchor a Npc_Keeper     锚点字母 → Objects 下的节点名
/// @spawn 1 From_S1_04      数字 → 出生点 ID（可选；左右出口的到达点不写时由生成工具按开口推算）
/// ---
/// （Rows 行，每行 Cols 个字符）
/// </code>
///
/// 坐标：第 c 列、第 r 行瓦片的左上角在房间局部像素 (c * 32, r * 32 - Shift)。
/// Shift 让瓦片按全局 32 像素对齐（房间顶边的全局 Y 不一定是 32 的倍数）。
/// 出生点和锚点字符表示“脚站在这一格里”：位置是这一格的底边中点（<see cref="FeetAt"/>），下面一格必须能站。
/// </summary>
public sealed class RoomGrid
{
    public const int Tile = 32;
    public const string DirectoryPath = "res://assets/data/world/rooms/";
    public const string Separator = "---";

    // ---------- 字符 ----------
    /// <summary>实心地形，可攀爬。</summary>
    public const char Solid = '#';
    /// <summary>实心地形，不可攀爬、不可蹬墙跳。</summary>
    public const char NoClimb = 'X';
    public const char Empty = '.';
    /// <summary>单向平台：从下面能跳上来，站上去不掉。</summary>
    public const char OneWay = '-';
    /// <summary>普通攻击能打碎的地形（可攀爬，打碎后变空）。</summary>
    public const char Breakable = 'B';
    /// <summary>隐藏的可破坏墙：外观和 # 一样、地图上不标；其余同 B。</summary>
    public const char HiddenBreakable = 'H';
    /// <summary>条件地形：@cond 的标记成立后才出现（出现后可攀爬）。</summary>
    public const char Conditional = 'R';
    /// <summary>默认出生点（必须恰好一个）。</summary>
    public const char DefaultSpawn = 'P';
    /// <summary>存档点（房间表 HasSavePoint 时必须恰好一个，否则不能有）。</summary>
    public const char SavePoint = 'S';

    public static string PathFor(string roomId) => DirectoryPath + roomId + ".txt";

    public string RoomId { get; private set; } = "";
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    /// <summary>瓦片层整体上移的像素数（0..31）。</summary>
    public int Shift { get; private set; }
    /// <summary>R 的出现条件；没有 R 时为空。</summary>
    public string Condition { get; private set; } = "";
    public IReadOnlyDictionary<char, string> Anchors => _anchors;
    public IReadOnlyDictionary<char, string> Spawns => _spawns;
    public IReadOnlyList<string> Comments => _comments;

    private char[,] _cells = new char[0, 0];
    private readonly Dictionary<char, string> _anchors = new();
    private readonly Dictionary<char, string> _spawns = new();
    private readonly List<string> _comments = new();

    // ---------- 尺寸 ----------

    public static int ShiftFor(RoomDef room, Vector2I cellSize) => Mathf.PosMod(room.Origin.Y * cellSize.Y, Tile);

    /// <summary>房间表规定的列数和行数：列 = 宽 ÷ 32；行 = ⌈(高 + Shift) ÷ 32⌉。</summary>
    public static Vector2I ExpectedSize(RoomDef room, Vector2I cellSize)
    {
        int width = room.Size.X * cellSize.X, height = room.Size.Y * cellSize.Y;
        if (width % Tile != 0) throw new InvalidOperationException($"{room.Id}：房间宽 {width} 不是 {Tile} 的倍数");
        int shift = ShiftFor(room, cellSize);
        return new Vector2I(width / Tile, (height + shift + Tile - 1) / Tile);
    }

    // ---------- 读格子 ----------

    public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < Cols && r < Rows;

    /// <summary>房间范围外一律当作空（出口开口外面就是另一个房间）。</summary>
    public char At(int c, int r) => InBounds(c, r) ? _cells[r, c] : Empty;

    public void Set(int c, int r, char ch)
    {
        if (!InBounds(c, r)) throw new ArgumentOutOfRangeException($"({c},{r}) 超出 {Cols}x{Rows}");
        _cells[r, c] = ch;
    }

    /// <summary>这个字符在给定条件下是不是实心（挡路、能站）。</summary>
    public static bool IsSolidChar(char ch, bool conditionMet) =>
        ch is Solid or NoClimb or Breakable or HiddenBreakable || (ch == Conditional && conditionMet);

    public static bool IsClimbableChar(char ch, bool conditionMet) =>
        ch is Solid or Breakable or HiddenBreakable || (ch == Conditional && conditionMet);

    public static bool IsBreakableChar(char ch) => ch is Breakable or HiddenBreakable;

    /// <summary>出生点、存档点、锚点、数字出生点：地形上都是空格。</summary>
    public static bool IsMarkerChar(char ch) =>
        ch is DefaultSpawn or SavePoint || (ch >= 'a' && ch <= 'z') || (ch >= '1' && ch <= '9');

    public static bool IsLegalChar(char ch) =>
        ch is Solid or NoClimb or Empty or OneWay or Breakable or HiddenBreakable or Conditional || IsMarkerChar(ch);

    public bool IsSolid(int c, int r, bool conditionMet = false) => IsSolidChar(At(c, r), conditionMet);

    // ---------- 坐标 ----------

    /// <summary>瓦片在房间局部像素里的矩形。</summary>
    public Rect2 TileRect(int c, int r) => new(c * Tile, r * Tile - Shift, Tile, Tile);

    /// <summary>脚站在这一格里时的位置：这一格底边的中点。</summary>
    public Vector2 FeetAt(int c, int r) => new(c * Tile + Tile / 2f, (r + 1) * Tile - Shift);

    /// <summary>某个字符第一次出现的位置（找不到返回 null）。</summary>
    public Vector2I? Find(char ch)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_cells[r, c] == ch) return new Vector2I(c, r);
        return null;
    }

    public IEnumerable<Vector2I> FindAll(char ch)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_cells[r, c] == ch) yield return new Vector2I(c, r);
    }

    // ---------- 新建、读、写 ----------

    /// <summary>按房间表新建一张全空的图（生成工具“原样转换”时用）。</summary>
    public static RoomGrid CreateEmpty(RoomDef room, Vector2I cellSize)
    {
        Vector2I size = ExpectedSize(room, cellSize);
        var grid = new RoomGrid { RoomId = room.Id, Cols = size.X, Rows = size.Y, Shift = ShiftFor(room, cellSize) };
        grid._cells = new char[size.Y, size.X];
        for (int r = 0; r < size.Y; r++)
            for (int c = 0; c < size.X; c++)
                grid._cells[r, c] = Empty;
        return grid;
    }

    public void SetCondition(string flag) => Condition = flag ?? "";
    public void SetAnchor(char letter, string nodeName) => _anchors[letter] = nodeName;
    public void SetSpawn(char digit, string spawnId) => _spawns[digit] = spawnId;
    public void AddComment(string text) => _comments.Add(text);

    /// <summary>
    /// 解析并校验。错误（任意一条）都返回 null，error 里列出全部问题（最多 20 条），不会生成半张图。
    /// 校验：@room 与房间表一致；@size 与 ExpectedSize 一致，行数、每行长度都对；字符合法；
    /// P 恰好一个；S 与 HasSavePoint 一致；锚点、数字出生点声明和使用一一对应且各用一次；有 R 必须有 @cond，反之亦然。
    /// </summary>
    public static RoomGrid Parse(string text, RoomDef room, Vector2I cellSize, out string error)
    {
        var problems = new List<string>();
        var grid = new RoomGrid { RoomId = room.Id, Shift = ShiftFor(room, cellSize) };
        Vector2I expected = ExpectedSize(room, cellSize);
        string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int i = 0;
        string declaredRoom = null;
        Vector2I? declaredSize = null;
        for (; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line == Separator) { i++; break; }
            if (line.Length == 0) continue;
            if (line.StartsWith("#")) { grid._comments.Add(line.TrimStart('#').Trim()); continue; }
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "@room" when parts.Length == 2: declaredRoom = parts[1]; break;
                case "@size" when parts.Length == 2:
                    string[] wh = parts[1].Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) declaredSize = new Vector2I(w, h);
                    else problems.Add($"第 {i + 1} 行：@size 格式应为 列x行");
                    break;
                case "@cond" when parts.Length == 2: grid.Condition = parts[1]; break;
                case "@anchor" when parts.Length == 3 && parts[1].Length == 1 && parts[1][0] is >= 'a' and <= 'z':
                    if (!grid._anchors.TryAdd(parts[1][0], parts[2])) problems.Add($"第 {i + 1} 行：锚点 {parts[1]} 重复声明");
                    break;
                case "@spawn" when parts.Length == 3 && parts[1].Length == 1 && parts[1][0] is >= '1' and <= '9':
                    if (!grid._spawns.TryAdd(parts[1][0], parts[2])) problems.Add($"第 {i + 1} 行：出生点 {parts[1]} 重复声明");
                    break;
                default: problems.Add($"第 {i + 1} 行：看不懂的文件头“{line}”"); break;
            }
        }
        if (declaredRoom != room.Id) problems.Add($"@room 应为 {room.Id}（实际 {declaredRoom ?? "没写"}）");
        if (declaredSize != expected) problems.Add($"@size 应为 {expected.X}x{expected.Y}（实际 {(declaredSize is { } s ? $"{s.X}x{s.Y}" : "没写")}）");

        var rows = new List<string>();
        for (; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length == 0 && lines.Skip(i).All(l => l.Trim().Length == 0)) break;
            rows.Add(line);
        }
        if (rows.Count != expected.Y) problems.Add($"行数应为 {expected.Y}（实际 {rows.Count}）");

        grid.Cols = expected.X;
        grid.Rows = expected.Y;
        grid._cells = new char[expected.Y, expected.X];
        for (int r = 0; r < expected.Y; r++)
        {
            string row = r < rows.Count ? rows[r] : "";
            if (r < rows.Count && row.Length != expected.X) problems.Add($"第 {r} 行长度应为 {expected.X}（实际 {row.Length}）");
            for (int c = 0; c < expected.X; c++)
            {
                char ch = c < row.Length ? row[c] : Empty;
                if (!IsLegalChar(ch)) { problems.Add($"({c},{r})：非法字符“{ch}”"); ch = Empty; }
                grid._cells[r, c] = ch;
            }
        }

        int Count(char ch) => grid.FindAll(ch).Count();
        if (Count(DefaultSpawn) != 1) problems.Add($"P（默认出生点）应恰好 1 个（实际 {Count(DefaultSpawn)}）");
        int saves = Count(SavePoint);
        if (room.HasSavePoint && saves != 1) problems.Add($"房间表有存档点，S 应恰好 1 个（实际 {saves}）");
        if (!room.HasSavePoint && saves != 0) problems.Add($"房间表没有存档点，不能有 S（实际 {saves}）");
        for (char ch = 'a'; ch <= 'z'; ch++)
        {
            int n = Count(ch);
            bool declared = grid._anchors.ContainsKey(ch);
            if (declared && n != 1) problems.Add($"锚点 {ch}（{grid._anchors[ch]}）应在图里恰好出现 1 次（实际 {n}）");
            if (!declared && n > 0) problems.Add($"图里有锚点 {ch}，但文件头没有 @anchor {ch}");
        }
        for (char ch = '1'; ch <= '9'; ch++)
        {
            int n = Count(ch);
            bool declared = grid._spawns.ContainsKey(ch);
            if (declared && n != 1) problems.Add($"出生点 {ch}（{grid._spawns[ch]}）应在图里恰好出现 1 次（实际 {n}）");
            if (!declared && n > 0) problems.Add($"图里有出生点 {ch}，但文件头没有 @spawn {ch}");
        }
        bool hasR = Count(Conditional) > 0;
        if (hasR && grid.Condition.Length == 0) problems.Add("图里有 R（条件地形），文件头必须写 @cond");
        if (!hasR && grid.Condition.Length > 0) problems.Add("文件头写了 @cond，但图里没有 R");

        if (problems.Count > 0)
        {
            error = $"{room.Id} 的文字图有 {problems.Count} 个问题：" + string.Join("；", problems.Take(20));
            return null;
        }
        error = "";
        return grid;
    }

    /// <summary>写回文字（Parse 的逆操作；Parse(Serialize()) 得到同一张图）。</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (string comment in _comments) sb.Append("# ").Append(comment).Append('\n');
        sb.Append($"@room {RoomId}\n@size {Cols}x{Rows}\n");
        if (Condition.Length > 0) sb.Append($"@cond {Condition}\n");
        foreach (var (letter, node) in _anchors.OrderBy(a => a.Key)) sb.Append($"@anchor {letter} {node}\n");
        foreach (var (digit, spawn) in _spawns.OrderBy(a => a.Key)) sb.Append($"@spawn {digit} {spawn}\n");
        sb.Append(Separator).Append('\n');
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++) sb.Append(_cells[r, c]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>从 res:// 读并解析；文件不存在时返回 null 且 error 为空。</summary>
    public static RoomGrid Load(RoomDef room, Vector2I cellSize, out string error)
    {
        string path = PathFor(room.Id);
        if (!FileAccess.FileExists(path)) { error = ""; return null; }
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return Parse(file.GetAsText(), room, cellSize, out error);
    }
}
