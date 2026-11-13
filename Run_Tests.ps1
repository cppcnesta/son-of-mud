param(
    [string]$GodotPath = $env:GODOT_DOTNET,
    [ValidateSet('all','core','world','player','combat','player-feel','wall-surface','hit-feel','flow','ui','narrative','enemy','levelcheck','new')][string]$Suite = 'all',
    [switch]$Headless
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Pass -GodotPath with the Godot 4.7.2 .NET console executable, or set GODOT_DOTNET.'
}
$version = (& $GodotPath --version | Out-String).Trim()
if ($version -notmatch '^4\.7\.2\..*mono') { throw "Godot 4.7.2 .NET required. Selected: $version" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required.' }
$projectPath = $PSScriptRoot
$runtimePath = Join-Path $projectPath '.runtime'
$outputPath = Join-Path $projectPath 'tests\output'
$testDataPath = Join-Path $runtimePath 'CSharpTestAppData'
New-Item -ItemType Directory -Force -Path $testDataPath | Out-Null
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$variableNames = @('APPDATA','LOCALAPPDATA','MUDCHILD_TEST_ISOLATED','DOTNET_CLI_HOME','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','MSBuildEnableWorkloadResolver')
$previousValues = @{}
foreach ($variableName in $variableNames) { $previousValues[$variableName] = [Environment]::GetEnvironmentVariable($variableName, 'Process') }
$resultCode = 1
# 逐个记录每个测试场景（Godot 进程）的退出码：结果文件全部通过、进程却在退出时崩溃，也要能看出来是哪个场景。
$sceneExits = New-Object System.Collections.Generic.List[object]
function Add-SceneExit([string]$scene, [int]$code) { $sceneExits.Add([pscustomobject]@{ Scene = $scene; Code = $code }) }
try {
    $env:DOTNET_CLI_HOME = Join-Path $runtimePath 'DotnetHome'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:MSBuildEnableWorkloadResolver = 'false'
    & dotnet build (Join-Path $projectPath 'SonOfMud.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "C# build failed ($LASTEXITCODE)." }
    $env:APPDATA = $testDataPath
    $env:LOCALAPPDATA = $testDataPath
    $env:MUDCHILD_TEST_ISOLATED = '1'
    & $GodotPath --headless --path $projectPath --editor --import --quit --log-file (Join-Path $outputPath 'csharp_import.log')
    if ($LASTEXITCODE -ne 0) { throw "Godot import failed ($LASTEXITCODE)." }
    $importIssues = Select-String -Path (Join-Path $outputPath 'csharp_import.log') -Pattern 'nonexistent section "preset\.' -SimpleMatch:$false -ErrorAction SilentlyContinue
    if ($importIssues) { Write-Host "IMPORT ERROR: export_presets.cfg has an empty section ($($importIssues.Count) lines)" -ForegroundColor Red }
    $coreCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'core') {
        # Core 验收测试：结果写到 tests/output/core_results.json
        $coreArguments = @('--path', $projectPath, '--scene', 'res://tests/core/core_suite.tscn', '--audio-driver', 'Dummy', '--log-file', (Join-Path $outputPath 'core_suite.log'))
        if ($Headless) { $coreArguments += '--headless' }
        & $GodotPath @coreArguments
        Add-SceneExit "res://tests/core/core_suite.tscn" $LASTEXITCODE
        $coreCode = $LASTEXITCODE
    }
    $worldCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'world' -or $Suite -eq 'new') {
        # World / Save / Camera 测试：每个场景单独运行，结果写到 tests/output/world_<名字>_results.json
        $worldScenes = @('tests/world/map_check.tscn', 'tests/world/slice_test.tscn', 'tests/world/door_test.tscn', 'tests/world/gate_test.tscn', 'tests/world/room_grid_test.tscn', 'tests/world/geometry_sync_test.tscn', 'tests/world/geometry_validate_test.tscn', 'tests/world/terrain_test.tscn', 'tests/save/save_roundtrip.tscn', 'tests/save/respawn_test.tscn', 'tests/integration/real_player_test.tscn', 'tests/world/background_test.tscn')
        foreach ($worldScene in $worldScenes) {
            $worldName = [System.IO.Path]::GetFileNameWithoutExtension($worldScene)
            $worldArguments = @('--path', $projectPath, '--scene', "res://$worldScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "world_$worldName.log"))
            if ($Headless) { $worldArguments += '--headless' }
            & $GodotPath @worldArguments
            Add-SceneExit "res://$worldScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $worldCode -eq 0) { $worldCode = $LASTEXITCODE }
        }
    }
    # 新玩家 / 战斗验收测试：结果写到 tests/output/player_results.json、combat_results.json、player_feel_results.json、player_art_results.json、hit_feel_results.json
    $newCode = 0
    $newScenes = [ordered]@{
        'player' = 'res://tests/player/player_suite.tscn'
        'combat' = 'res://tests/combat/combat_suite.tscn'
        'player-feel' = 'res://tests/player/feel_checks.tscn'
        'wall-surface' = 'res://tests/player/wall_surface_test.tscn'
        'player-art' = 'res://tests/player/player_art.tscn'
        'hit-feel' = 'res://tests/combat/hit_feel.tscn'
    }
    foreach ($name in $newScenes.Keys) {
        if ($Suite -ne 'all' -and $Suite -ne 'new' -and $Suite -ne $name) { continue }
        $logName = ($name -replace '-', '_') + '_suite.log'
        $newArguments = @('--path', $projectPath, '--scene', $newScenes[$name], '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath $logName))
        if ($Headless) { $newArguments += '--headless' }
        & $GodotPath @newArguments
        Add-SceneExit "$($newScenes[$name])" $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) { $newCode = $LASTEXITCODE }
    }
    # 开局流程测试（一律从正式入口 main_game.tscn 启动）：每个场景单独运行，结果写到 tests/output/flow_<名字>_results.json
    $flowCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'flow' -or $Suite -eq 'new') {
        $flowScenes = @('tests/flow/input_bindings.tscn', 'tests/flow/boot_new_game.tscn', 'tests/flow/boot_continue.tscn', 'tests/flow/boot_corrupt_save.tscn', 'tests/flow/boot_unsupported_version.tscn', 'tests/flow/restart_run.tscn', 'tests/player/interact_test.tscn', 'tests/flow/region_map_test.tscn', 'tests/player/bread_health.tscn', 'tests/flow/hit_feel_flow.tscn')
        foreach ($flowScene in $flowScenes) {
            $flowName = [System.IO.Path]::GetFileNameWithoutExtension($flowScene)
            $flowArguments = @('--path', $projectPath, '--scene', "res://$flowScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "flow_$flowName.log"))
            if ($Headless) { $flowArguments += '--headless' }
            & $GodotPath @flowArguments
            Add-SceneExit "res://$flowScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $flowCode -eq 0) { $flowCode = $LASTEXITCODE }
        }
    }
    # UI 测试（HUD 假事件、暂停菜单 / 地图、新主菜单流程、读档失败、真实玩家集成）：结果写到 tests/output/ui_<名字>_results.json
    $uiCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'ui' -or $Suite -eq 'new') {
        $uiScenes = @('tests/ui/ui_hud_test.tscn', 'tests/ui/ui_pause_map_test.tscn', 'tests/ui/title_menu_test.tscn', 'tests/ui/title_menu_failure_test.tscn', 'tests/ui/ui_integration_test.tscn', 'tests/ui/quit_from_pause_test.tscn', 'tests/ui/quit_from_title_test.tscn', 'tests/ui/legacy_save_climb_test.tscn')
        foreach ($uiScene in $uiScenes) {
            $uiName = [System.IO.Path]::GetFileNameWithoutExtension($uiScene)
            $uiArguments = @('--path', $projectPath, '--scene', "res://$uiScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "ui_$uiName.log"))
            if ($Headless) { $uiArguments += '--headless' }
            & $GodotPath @uiArguments
            Add-SceneExit "res://$uiScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $uiCode -eq 0) { $uiCode = $LASTEXITCODE }
        }
    }
    # 剧情测试：narrative_unit 不需要游戏场景；其余从正式入口 main_game.tscn 启动、用真实玩家。
    # 结果写到 tests/output/narrative_unit_results.json 和 flow_narrative_<名字>_results.json
    $narrativeCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'narrative' -or $Suite -eq 'new') {
        $narrativeScenes = @('tests/narrative/narrative_unit.tscn', 'tests/narrative/narrative_dialog.tscn', 'tests/narrative/narrative_choice.tscn', 'tests/narrative/narrative_map1.tscn', 'tests/narrative/narrative_restart.tscn')
        foreach ($narrativeScene in $narrativeScenes) {
            $narrativeName = [System.IO.Path]::GetFileNameWithoutExtension($narrativeScene)
            $narrativeArguments = @('--path', $projectPath, '--scene', "res://$narrativeScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "$narrativeName.log"))
            if ($Headless) { $narrativeArguments += '--headless' }
            & $GodotPath @narrativeArguments
            Add-SceneExit "res://$narrativeScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $narrativeCode -eq 0) { $narrativeCode = $LASTEXITCODE }
        }
    }
    # 敌人测试（第 4 轮）：enemy_unit / enemy_lab / keeper_lab 结果写到 tests/output/<名字>_results.json，devout_lab 写到 enemy_devout_lab_results.json；
    # enemy_flow / keeper_flow 从正式入口 main_game.tscn 启动，结果写到 tests/output/enemy_flow_results.json、enemy_keeper_flow_results.json；
    # 扔泥肝 liver_throw 也从正式入口启动（真实玩家、真实按键），结果写到 enemy_liver_throw_results.json
    # 敌人美术图自动载入 enemy_art，结果写到 tests/output/enemy_art_results.json
    $enemyCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'enemy' -or $Suite -eq 'new') {
        $enemyScenes = @('tests/enemy/enemy_unit.tscn', 'tests/enemy/enemy_lab.tscn', 'tests/enemy/keeper_lab.tscn', 'tests/enemy/enemy_flow.tscn', 'tests/enemy/keeper_flow.tscn', 'tests/enemy/arena_lock_climb.tscn', 'tests/enemy/devout_lab.tscn', 'tests/player/liver_throw.tscn', 'tests/enemy/enemy_art.tscn')
        foreach ($enemyScene in $enemyScenes) {
            $enemyName = [System.IO.Path]::GetFileNameWithoutExtension($enemyScene)
            $enemyArguments = @('--path', $projectPath, '--scene', "res://$enemyScene", '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "enemy_$enemyName.log"))
            if ($Headless) { $enemyArguments += '--headless' }
            & $GodotPath @enemyArguments
            Add-SceneExit "res://$enemyScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $enemyCode -eq 0) { $enemyCode = $LASTEXITCODE }
        }
    }
    # 关卡几何：静态可达性检查（fixtures）和模拟器校准 + 路线机器人（真实玩家、真实按键）。
    # 结果写到 tests/output/levelcheck_results.json、levelcheck_calibration_results.json
    $levelCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'levelcheck' -or $Suite -eq 'new') {
        $levelScenes = @('tests/levelcheck/levelcheck_suite.tscn', 'tests/levelcheck/levelcheck_calibration.tscn', 'tests/levelcheck/levelcheck_map1.tscn', 'tests/levelcheck/levelcheck_map1_walk.tscn')
        foreach ($levelScene in $levelScenes) {
            $levelName = [System.IO.Path]::GetFileNameWithoutExtension($levelScene)
            $levelArguments = @('--path', $projectPath, '--scene', "res://$levelScene", '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "$levelName.log"))
            if ($Headless) { $levelArguments += '--headless' }
            & $GodotPath @levelArguments
            Add-SceneExit "res://$levelScene" $LASTEXITCODE
            if ($LASTEXITCODE -ne 0 -and $levelCode -eq 0) { $levelCode = $LASTEXITCODE }
        }
    }
    $badExits = @($sceneExits | Where-Object { $_.Code -ne 0 })
    ($sceneExits | ForEach-Object { "$($_.Code)`t$($_.Scene)" }) | Set-Content -Path (Join-Path $outputPath 'scene_exit_codes.txt') -Encoding utf8
    Write-Host "Scenes run: $($sceneExits.Count), non-zero exit codes: $($badExits.Count)"
    foreach ($bad in $badExits) { Write-Host "  EXIT $($bad.Code): $($bad.Scene)" -ForegroundColor Red }
    $resultCode = if ($coreCode -ne 0) { $coreCode } elseif ($worldCode -ne 0) { $worldCode } elseif ($flowCode -ne 0) { $flowCode } elseif ($uiCode -ne 0) { $uiCode } elseif ($narrativeCode -ne 0) { $narrativeCode } elseif ($enemyCode -ne 0) { $enemyCode } elseif ($levelCode -ne 0) { $levelCode } else { $newCode }
    if ($resultCode -eq 0 -and $badExits.Count -gt 0) { $resultCode = $badExits[0].Code }
    if ($resultCode -eq 0 -and $importIssues) { $resultCode = 3 }
}
finally {
    foreach ($variableName in $variableNames) { [Environment]::SetEnvironmentVariable($variableName, $previousValues[$variableName], 'Process') }
}
exit $resultCode
