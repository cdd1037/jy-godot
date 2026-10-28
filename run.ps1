param(
    [string]$Godot = "godot",
    [Alias("Library")][string]$Package,
    [string]$Source,
    [int]$Scene = 70,
    [switch]$Smoke,
    [string]$Capture,
    [string]$Report,
    [switch]$Headless,
    [switch]$GridMovement,
    [switch]$PlayOpening,
    [switch]$EventDemo,
    [switch]$TravelSmoke,
    [string]$StoryOverlay
)
$ErrorActionPreference = "Stop"
if (-not $Package -and -not $Source) {
    $defaultPackage = Join-Path $PSScriptRoot "content\base"
    if (Test-Path -LiteralPath (Join-Path $defaultPackage "manifest.json")) { $Package = $defaultPackage }
}
if ($EventDemo) {
    if ($StoryOverlay) { throw "Use either -EventDemo or -StoryOverlay." }
    if ($Scene -ne 70) { throw "-EventDemo requires scene 70." }
    $StoryOverlay = Join-Path $PSScriptRoot "content\examples\event-demo.json"
}
if ($Package -and $Source) { throw "Use either -Library/-Package or -Source." }
if ($TravelSmoke -and (-not $Smoke -or $EventDemo -or $Scene -ne 70)) { throw "-TravelSmoke requires -Smoke, scene 70, and no -EventDemo." }
$arguments = @("--path", (Join-Path $PSScriptRoot "godot"))
if ($Smoke) { $arguments += @("--fixed-fps", "60") }
if ($Headless) { $arguments += "--headless" }
$arguments += "--"
if ($Package) { $arguments += @("--library", [IO.Path]::GetFullPath($Package)) }
if ($Source) { $arguments += @("--source", [IO.Path]::GetFullPath($Source)) }
$arguments += @("--scene", "$Scene")
if ($StoryOverlay) { $arguments += @("--story-overlay", [IO.Path]::GetFullPath($StoryOverlay)) }
if ($Smoke) { $arguments += "--smoke-test" }
if ($GridMovement) { $arguments += "--grid-movement" }
if ($PlayOpening) { $arguments += "--play-opening" }
if ($EventDemo -and $Smoke) { $arguments += "--event-smoke" }
if ($TravelSmoke) { $arguments += "--travel-smoke" }
if ($Capture) { $arguments += @("--capture", [IO.Path]::GetFullPath($Capture)) }
if ($Report) { $arguments += @("--report", [IO.Path]::GetFullPath($Report)) }
& $Godot @arguments
exit $LASTEXITCODE
