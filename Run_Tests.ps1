param(
    [string]$GodotPath = $env:GODOT_DOTNET,
    [ValidateSet('all','core','world','player','combat','player-feel','flow','ui','narrative','enemy','new','feel','scene-paths','jump-landing','attacks','stairs','focused')][string]$Suite = 'all',
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
    $legacyCode = 0
    $newSuites = @('world','player','combat','player-feel','flow','ui','narrative','enemy','new')
    if ($Suite -ne 'core' -and $newSuites -notcontains $Suite) {
        $arguments = @('--path', $projectPath, '--scene', 'res://tests/runtime_suite.tscn', '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--max-fps', '240', '--log-file', (Join-Path $outputPath 'csharp_runtime_suite.log'))
        if ($Headless) { $arguments += '--headless' }
        if ($Suite -eq 'focused') { $arguments += @('--', '--focused') }
        elseif ($Suite -ne 'all') { $arguments += @('--', "--$Suite-only") }
        & $GodotPath @arguments
        $legacyCode = $LASTEXITCODE
    }
    $coreCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'core') {
        # Core 验收测试：结果写到 tests/output/core_results.json
        $coreArguments = @('--path', $projectPath, '--scene', 'res://tests/core/core_suite.tscn', '--audio-driver', 'Dummy', '--log-file', (Join-Path $outputPath 'core_suite.log'))
        if ($Headless) { $coreArguments += '--headless' }
        & $GodotPath @coreArguments
        $coreCode = $LASTEXITCODE
    }
    $worldCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'world' -or $Suite -eq 'new') {
        # World / Save / Camera 测试：每个场景单独运行，结果写到 tests/output/world_<名字>_results.json
        $worldScenes = @('tests/world/map_check.tscn', 'tests/world/slice_test.tscn', 'tests/world/door_test.tscn', 'tests/world/gate_test.tscn', 'tests/save/save_roundtrip.tscn', 'tests/save/respawn_test.tscn', 'tests/integration/real_player_test.tscn')
        foreach ($worldScene in $worldScenes) {
            $worldName = [System.IO.Path]::GetFileNameWithoutExtension($worldScene)
            $worldArguments = @('--path', $projectPath, '--scene', "res://$worldScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "world_$worldName.log"))
            if ($Headless) { $worldArguments += '--headless' }
            & $GodotPath @worldArguments
            if ($LASTEXITCODE -ne 0 -and $worldCode -eq 0) { $worldCode = $LASTEXITCODE }
        }
    }
    # 新玩家 / 战斗验收测试：结果写到 tests/output/player_results.json、combat_results.json、player_feel_results.json
    $newCode = 0
    $newScenes = [ordered]@{
        'player' = 'res://tests/player/player_suite.tscn'
        'combat' = 'res://tests/combat/combat_suite.tscn'
        'player-feel' = 'res://tests/player/feel_checks.tscn'
    }
    foreach ($name in $newScenes.Keys) {
        if ($Suite -ne 'all' -and $Suite -ne 'new' -and $Suite -ne $name) { continue }
        $logName = ($name -replace '-', '_') + '_suite.log'
        $newArguments = @('--path', $projectPath, '--scene', $newScenes[$name], '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath $logName))
        if ($Headless) { $newArguments += '--headless' }
        & $GodotPath @newArguments
        if ($LASTEXITCODE -ne 0) { $newCode = $LASTEXITCODE }
    }
    # 开局流程测试（一律从正式入口 main_game.tscn 启动）：每个场景单独运行，结果写到 tests/output/flow_<名字>_results.json
    $flowCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'flow' -or $Suite -eq 'new') {
        $flowScenes = @('tests/flow/input_bindings.tscn', 'tests/flow/boot_new_game.tscn', 'tests/flow/boot_continue.tscn', 'tests/flow/boot_corrupt_save.tscn', 'tests/flow/boot_unsupported_version.tscn', 'tests/flow/restart_run.tscn', 'tests/player/interact_test.tscn')
        foreach ($flowScene in $flowScenes) {
            $flowName = [System.IO.Path]::GetFileNameWithoutExtension($flowScene)
            $flowArguments = @('--path', $projectPath, '--scene', "res://$flowScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "flow_$flowName.log"))
            if ($Headless) { $flowArguments += '--headless' }
            & $GodotPath @flowArguments
            if ($LASTEXITCODE -ne 0 -and $flowCode -eq 0) { $flowCode = $LASTEXITCODE }
        }
    }
    # UI 测试（HUD 假事件、暂停菜单 / 地图、新主菜单流程、读档失败、真实玩家集成）：结果写到 tests/output/ui_<名字>_results.json
    $uiCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'ui' -or $Suite -eq 'new') {
        $uiScenes = @('tests/ui/ui_hud_test.tscn', 'tests/ui/ui_pause_map_test.tscn', 'tests/ui/title_menu_test.tscn', 'tests/ui/title_menu_failure_test.tscn', 'tests/ui/ui_integration_test.tscn')
        foreach ($uiScene in $uiScenes) {
            $uiName = [System.IO.Path]::GetFileNameWithoutExtension($uiScene)
            $uiArguments = @('--path', $projectPath, '--scene', "res://$uiScene", '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "ui_$uiName.log"))
            if ($Headless) { $uiArguments += '--headless' }
            & $GodotPath @uiArguments
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
            if ($LASTEXITCODE -ne 0 -and $narrativeCode -eq 0) { $narrativeCode = $LASTEXITCODE }
        }
    }
    # 敌人测试（第 4 轮）：enemy_unit / enemy_lab / keeper_lab 结果写到 tests/output/<名字>_results.json；
    # enemy_flow / keeper_flow 从正式入口 main_game.tscn 启动，结果写到 tests/output/enemy_flow_results.json、enemy_keeper_flow_results.json
    $enemyCode = 0
    if ($Suite -eq 'all' -or $Suite -eq 'enemy' -or $Suite -eq 'new') {
        $enemyScenes = @('tests/enemy/enemy_unit.tscn', 'tests/enemy/enemy_lab.tscn', 'tests/enemy/keeper_lab.tscn', 'tests/enemy/enemy_flow.tscn', 'tests/enemy/keeper_flow.tscn')
        foreach ($enemyScene in $enemyScenes) {
            $enemyName = [System.IO.Path]::GetFileNameWithoutExtension($enemyScene)
            $enemyArguments = @('--path', $projectPath, '--scene', "res://$enemyScene", '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--log-file', (Join-Path $outputPath "enemy_$enemyName.log"))
            if ($Headless) { $enemyArguments += '--headless' }
            & $GodotPath @enemyArguments
            if ($LASTEXITCODE -ne 0 -and $enemyCode -eq 0) { $enemyCode = $LASTEXITCODE }
        }
    }
    $resultCode = if ($legacyCode -ne 0) { $legacyCode } elseif ($coreCode -ne 0) { $coreCode } elseif ($worldCode -ne 0) { $worldCode } elseif ($flowCode -ne 0) { $flowCode } elseif ($uiCode -ne 0) { $uiCode } elseif ($narrativeCode -ne 0) { $narrativeCode } elseif ($enemyCode -ne 0) { $enemyCode } else { $newCode }
}
finally {
    foreach ($variableName in $variableNames) { [Environment]::SetEnvironmentVariable($variableName, $previousValues[$variableName], 'Process') }
}
exit $resultCode
