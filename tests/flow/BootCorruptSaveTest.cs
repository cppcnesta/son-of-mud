using System.Text;
using SonOfMud.Save;

/// <summary>
/// 存档损坏：往存档槽写一段乱码，从正式入口选“继续”。
/// 附加一个用例：格式正确但房间不存在（S9_99）的存档，换房失败同样按开局失败处理。
/// </summary>
public partial class BootCorruptSaveTest : BootFailureTestBase
{
    protected override string SuiteName => "boot_corrupt_save";

    protected override (string name, byte[] bytes)[] Cases()
    {
        var garbage = new byte[257];
        var rng = new System.Random(20261110);
        rng.NextBytes(garbage);
        garbage[0] = 0xFF; // 保证不是合法的 VarToStr 文本开头

        var unknownRoom = new SaveData { Room = "S9_99", SavePoint = "S9_99" };
        byte[] unknownBytes = Encoding.UTF8.GetBytes(Godot.GD.VarToStr(unknownRoom.ToDictionary()));
        return new[] { ("乱码", garbage), ("房间不存在", unknownBytes) };
    }
}
