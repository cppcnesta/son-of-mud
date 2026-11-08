param(
    [string]$GodotPath = $env:GODOT_DOTNET,
    [ValidateSet('all','feel','scene-paths','jump-landing','attacks','stairs','focused')][string]$Suite = 'all',
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
$testDataPath = Join-Path $runtimePath 'CSharpTestAppData'
New-Item -ItemType Directory -Force -Path $testDataPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath 'Documentation') | Out-Null
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
    & $GodotPath --headless --path $projectPath --editor --import --quit --log-file (Join-Path $projectPath 'Documentation\csharp_import.log')
    if ($LASTEXITCODE -ne 0) { throw "Godot import failed ($LASTEXITCODE)." }
    $arguments = @('--path', $projectPath, '--scene', 'res://tests/runtime_suite.tscn', '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--fixed-fps', '60', '--max-fps', '240', '--log-file', (Join-Path $projectPath 'Documentation\csharp_runtime_suite.log'))
    if ($Headless) { $arguments += '--headless' }
    if ($Suite -eq 'focused') { $arguments += @('--', '--focused') }
    elseif ($Suite -ne 'all') { $arguments += @('--', "--$Suite-only") }
    & $GodotPath @arguments
    $resultCode = $LASTEXITCODE
}
finally {
    foreach ($variableName in $variableNames) { [Environment]::SetEnvironmentVariable($variableName, $previousValues[$variableName], 'Process') }
}
exit $resultCode
