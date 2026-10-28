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
    [switch]$DirectPlay,
    [switch]$DebugTools,
    [switch]$EventDemo,
    [switch]$BattleDemo,
    [switch]$BattleSmoke,
    [switch]$TravelSmoke,
    [switch]$SaveSmoke,
    [switch]$ItemActionsSmoke,
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
if ($BattleDemo) {
    if ($StoryOverlay -or $EventDemo -or $Scene -ne 70) { throw "-BattleDemo requires scene 70 and no other story overlay." }
    $StoryOverlay = Join-Path $PSScriptRoot "content/examples/battle-demo.json"
}
if ($BattleSmoke -and (-not $Smoke -or -not $BattleDemo -or $TravelSmoke)) { throw "-BattleSmoke requires -Smoke -BattleDemo and no -TravelSmoke." }
if ($SaveSmoke -and (-not $Smoke -or -not $EventDemo -or $TravelSmoke -or $BattleSmoke -or $PlayOpening)) {
    throw "-SaveSmoke requires -Smoke -EventDemo and no other smoke mode or -PlayOpening."
}
if ($ItemActionsSmoke -and (-not $Smoke -or -not $BattleDemo -or $BattleSmoke -or $TravelSmoke -or $SaveSmoke -or $PlayOpening)) {
    throw "-ItemActionsSmoke requires -Smoke -BattleDemo and no other smoke mode or -PlayOpening."
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
if ($DirectPlay) { $arguments += "--direct-play" }
if ($DebugTools) { $arguments += "--debug-tools" }
if ($EventDemo -and $Smoke) { $arguments += "--event-smoke" }
if ($TravelSmoke) { $arguments += "--travel-smoke" }
if ($BattleSmoke) { $arguments += "--battle-smoke" }
if ($ItemActionsSmoke) { $arguments += "--item-actions-smoke" }
if ($SaveSmoke) { $arguments += @("--save-smoke", "--saves-directory", (Join-Path $PSScriptRoot ".local\saves-smoke-$([Guid]::NewGuid().ToString('N'))")) }
if ($Capture) { $arguments += @("--capture", [IO.Path]::GetFullPath($Capture)) }
if ($Report) { $arguments += @("--report", [IO.Path]::GetFullPath($Report)) }
& $Godot @arguments
exit $LASTEXITCODE
