using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// 按键（D13）：读 InputMap 检查 jump 只绑空格；move_up 还绑着 W；interact 是 E；
/// dialog_advance = E、空格、鼠标左键；map = M、Tab；throw_item（扔泥肝）= R，而且 R 不再绑给别的动作。
/// </summary>
public partial class InputBindingsTest : FlowTestBase
{
    protected override string SuiteName => "input_bindings";

    protected override Task RunChecks()
    {
        Expect("jump", "key:Space");
        Expect("move_up", "key:W");
        Expect("interact", "key:E");
        Expect("dialog_advance", "key:E", "key:Space", "mouse:1");
        Expect("map", "key:M", "key:Tab");
        Check(!Bindings("jump").Contains("key:W"), "jump 不再绑定 W（W 只管“上”和攀墙）");
        Expect("throw_item", "key:R");
        string[] onR = InputMap.GetActions().Where(a => Bindings(a).Contains("key:R")).Select(a => a.ToString()).ToArray();
        Check(onR.SequenceEqual(new[] { "throw_item" }), $"R 只绑给 throw_item（扔泥肝），没有别的动作抢 R（实际 {string.Join("、", onR)}）");
        return Task.CompletedTask;
    }

    private void Expect(string action, params string[] expected)
    {
        string[] actual = Bindings(action);
        bool ok = InputMap.HasAction(action) && actual.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s));
        Check(ok, $"{action} 的绑定正好是 {string.Join("、", expected)}（实际 {string.Join("、", actual)}）");
    }

    private static string[] Bindings(string action)
    {
        if (!InputMap.HasAction(action)) return System.Array.Empty<string>();
        return InputMap.ActionGetEvents(action).Select(Describe).ToArray();
    }

    private static string Describe(InputEvent e) => e switch
    {
        InputEventKey key => "key:" + OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode),
        InputEventMouseButton mouse => "mouse:" + (int)mouse.ButtonIndex,
        _ => e.GetClass(),
    };
}
