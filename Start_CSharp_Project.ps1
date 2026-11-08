param([string]$GodotPath = $env:GODOT_DOTNET)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Pass -GodotPath with the Godot 4.7.2 .NET executable, or set GODOT_DOTNET.'
}
$version = (& $GodotPath --version | Out-String).Trim()
if ($version -notmatch '^4\.7\.2\..*mono') { throw "Godot 4.7.2 .NET required. Selected: $version" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required.' }
& dotnet build (Join-Path $PSScriptRoot 'SonOfMud.csproj') -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "C# build failed ($LASTEXITCODE)." }
Start-Process -FilePath $GodotPath -ArgumentList @('--editor', '--path', ('"' + $PSScriptRoot + '"')) -WindowStyle Hidden
