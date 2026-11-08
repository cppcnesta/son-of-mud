using Godot;
using GDictionary = Godot.Collections.Dictionary;

public sealed class MudWhiteboxEvents
{
    public MudWhiteboxRoom Room { get; set; }
    public void Perform(string key, MudRoomEvent source)
    {
        var progress = MudProgress.Instance;
        GDictionary info = new();
        foreach (var value in Room.Data.Array("events"))
        {
            var entry = value.AsGodotDictionary();
            if (entry.Str("id") == key) { info = entry; break; }
        }
        if (info.Count == 0) return;
        if (info.ContainsKey("ability")) progress.Unlock(info.Str("ability"));
        if (key.StartsWith("gate_"))
        {
            var number = int.Parse(key.Substring("gate_".Length));
            if (number > 1 && !progress.Flag($"gate_{number - 1}"))
            { Room.ShowMessage("请从下方向上，先完成前一道门。"); return; }
            progress.Mark(key);
            if (number == 7) progress.Mark("seven_gates");
        }
        switch (key)
        {
            case "rest": Room.Player.Health = Room.Player.Rules.PlayerHealth; progress.SetCheckpoint(Room.RoomId, "Start"); break;
            case "arena": Room.StartBoss(); break;
            case "igi": progress.Mark("met_igi"); break;
            case "flood":
                if (!progress.Flag("flood_done")) { progress.Mark("flood_started"); Room.FloodTimer = 3f; }
                break;
            case "after_flood":
                if (!progress.Flag("flood_done")) { Room.ShowMessage("先操作水闸，等待洪水退去。"); return; }
                break;
            case "omen_1":
                if (!progress.HasAbility("dive") || Room.Player.Form != 0 || Room.Player.GlobalPosition.Y < Room.FloorY - 140f)
                { Room.ShowMessage("需要潜游能力，并以水形态抵达池底。"); return; }
                progress.Mark("district_C04"); break;
            case "omen_2":
                if (!progress.Flag("obstacle_C05")) { Room.ShowMessage("先用重压下砸打碎裂板。"); return; }
                progress.Mark("district_C05"); break;
            case "sigil_1": Room.LocalSequence = 1; break;
            case "sigil_2":
                Room.LocalSequence = Room.LocalSequence == 1 ? 2 : 0;
                Room.ShowMessage(Room.LocalSequence == 2 ? "征兆②响应" : "顺序不符，请从①重新开始。"); return;
            case "sigil_3":
                if (Room.LocalSequence != 2) { Room.LocalSequence = 0; Room.ShowMessage("顺序不符，请从①重新开始。"); return; }
                progress.Mark("district_C06"); break;
            case "truth":
            case "ending_awake":
            case "ending_sleep":
                if (!progress.Flag("boss_H03")) { Room.ShowMessage("先击败心脏前的敌人。"); return; }
                if (key == "truth") progress.Mark("truth");
                else if (!progress.Flag("truth")) { Room.ShowMessage("还有尚未拾起的记忆。"); return; }
                else if (key == "ending_sleep" && progress.Flag("ending_sleep"))
                { progress.NewGame(); MudRoomTravel.Instance.EnterRoom("M01", "Start", false); return; }
                else progress.Mark(key);
                break;
            case "test_all": Room.DebugUnlock(); break;
            case "test_clear":
                progress.TestAbilities.Clear();
                foreach (var ability in new[] { "double_jump", "wall_climb", "heavy_slam", "dive", "high_jump_upgrade" }) progress.TestAbilities[ability] = false;
                break;
            case "test_source": Room.SourceEnabled = !Room.SourceEnabled; break;
            case "test_target":
                foreach (var enemy in Room.GetTree().GetNodesInGroup("damageables")) enemy.QueueFree();
                Room.SpawnEnemy(new Vector2(1940, Room.FloorY - 32), "训练靶", "dummy", 400); break;
            case "test_exit": Room.OpenMenu(); break;
        }
        source.Used = true;
        source.QueueRedraw();
        if (!progress.TestMode) progress.Mark(Room.RoomId + ":" + key);
        Room.ShowMessage(info.Str("text", "征兆已响应。"));
    }
}
