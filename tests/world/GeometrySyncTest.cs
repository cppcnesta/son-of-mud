using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Tools;
using SonOfMud.World;

/// <summary>
/// 同步测试（关卡几何计划 v3 第 3.2 节“可以重复跑”）：对全部房间跑 BuildWorld --geometry 的干跑，必须“没有任何变化”，
/// 证明文字图和房间场景一致、生成可重复。有变化时列出房间和节点。
/// </summary>
public partial class GeometrySyncTest : WorldTestBase
{
    protected override string SuiteName => "geometry_sync";

    protected override Task RunChecks()
    {
        var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        int texts = layout.Rooms.Count(r => FileAccess.FileExists(RoomGrid.PathFor(r.Id)));
        Check(texts == 27, $"27 个房间都有文字图（实际 {texts}）");

        GeometryBuilder builder = GeometryBuilder.ForProject();
        bool ok = builder.RunGeometry(write: false);
        Check(ok && builder.Errors.Count == 0, "干跑：全部文字图通过校验（开口、封边、出生点、锚点、跨房间对齐）" + (builder.Errors.Count > 0 ? "：" + string.Join("；", builder.Errors.Take(6)) : ""));
        string[] rooms = builder.Changes.Select(c => c.Split('：')[0]).Distinct().ToArray();
        Check(builder.Changes.Count == 0, "干跑：没有任何变化（文字图和场景一致）" + (builder.Changes.Count > 0 ? $"，会变化的房间：{string.Join(",", rooms)}；{string.Join("；", builder.Changes.Take(8))}" : ""));
        Check(builder.Written.Count == 0, "干跑：没有写任何文件");
        Check(builder.Notes.All(n => !n.Contains("条件门")), "条件门都在左右出口上，位置由生成工具管理" + (builder.Notes.Count > 0 ? "（" + string.Join("；", builder.Notes) + "）" : ""));
        return Task.CompletedTask;
    }
}
