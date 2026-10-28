param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Output,
    [switch]$MapsOnly
)
$ErrorActionPreference = "Stop"
if (-not $Output) { $Output = Join-Path $PSScriptRoot "content\base" }
$importSource = [IO.Path]::GetFullPath($Source)
$importOutput = [IO.Path]::GetFullPath($Output)
$importTool = Join-Path $PSScriptRoot "tools\Game.Tools\bin\Debug\net10.0\Game.Tools.dll"
if (-not (Test-Path -LiteralPath $importTool)) {
    & (Join-Path $PSScriptRoot "build.ps1") -SkipTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$importArguments = @($importTool, "import", "--source", $importSource, "--output", $importOutput)
if ($MapsOnly) { $importArguments += "--maps-only" }
& dotnet @importArguments
exit $LASTEXITCODE
