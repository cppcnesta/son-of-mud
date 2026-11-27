using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 收集页左边的一个人物页，列在 assets/data/ui/character_pages.tres。
/// 遇到 = MetFlag 这个进度变量为真；MetFlag 为空的（主角）一开始就有。立绘是全身立绘，按比例放进收集页左边的立绘框。
/// </summary>
[GlobalClass]
public partial class CharacterPage : KeptResource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public Texture2D Portrait { get; set; }
    /// <summary>判断「遇到过」的进度变量（伊吉：talked.igi，第一次和他说完话时剧本设）；为空表示一开始就有。</summary>
    [Export] public StringName MetFlag { get; set; } = "";

    public bool Met(ProgressStore progress) => string.IsNullOrEmpty(MetFlag) || progress.Flag(MetFlag);
}
