param([string]$GodotPath = 'D:\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64_console.exe')
$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
if (-not (Test-Path -LiteralPath $GodotPath)) { throw "Godot not found: $GodotPath. Pass -GodotPath." }
$testDataPath = Join-Path $projectPath '.runtime\TestAppData'
$outputPath = Join-Path $projectPath 'tests\output'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
New-Item -ItemType Directory -Force -Path $testDataPath | Out-Null
$env:APPDATA = $testDataPath
$env:LOCALAPPDATA = $testDataPath
& $GodotPath --headless --path $projectPath --editor --import --quit --log-file (Join-Path $outputPath 'import.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $GodotPath --path $projectPath --scene 'res://tests/runtime_suite.tscn' --rendering-method gl_compatibility --audio-driver Dummy --fixed-fps 60 --max-fps 240 --log-file (Join-Path $outputPath 'runtime_suite.log')
exit $LASTEXITCODE
