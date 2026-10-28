param([switch]$SkipTests, [string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    & dotnet build JinYong.slnx --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $SkipTests) {
        & dotnet test tests/Game.Tests/Game.Tests.csproj --configuration $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
} finally { Pop-Location }
