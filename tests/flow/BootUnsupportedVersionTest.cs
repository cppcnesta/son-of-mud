using System.Text;
using SonOfMud.Save;

/// <summary>版本不支持：写一个 version = 99、其余字段都正确的存档，从正式入口选“继续”。</summary>
public partial class BootUnsupportedVersionTest : BootFailureTestBase
{
    protected override string SuiteName => "boot_unsupported_version";

    protected override (string name, byte[] bytes)[] Cases()
    {
        var data = new SaveData { Version = 99, Room = "S1_04", SavePoint = "S1_04" };
        byte[] bytes = Encoding.UTF8.GetBytes(Godot.GD.VarToStr(data.ToDictionary()));
        return new[] { ("版本 99", bytes) };
    }
}
