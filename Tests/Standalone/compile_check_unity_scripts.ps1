# EXTERNAL compile check of the project's hand-written C# against the Unity Editor's own managed assemblies,
# using the Roslyn compiler bundled with the Editor (C# 9, like Unity 6). Output goes to $env:TEMP only.
# This is NOT the Unity Editor's compile: it ignores asmdefs, packages and Unity's analyzers/IL post-processing.
# It does catch syntax errors and missing/renamed Unity APIs. Safe to run while the Editor is open.
#
# The inputs are an EXPLICIT allow-list (below). Before compiling, the script:
#   1. fails if any listed file is missing, and
#   2. fails if Assets/Scripts or Assets/Editor contain a .cs file that is not listed (and vice versa),
# so a wrong glob or a forgotten new file can no longer pass silently. When you add a script, add it here.
param([string]$UnityEditorData = "C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data")

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Paths relative to the repository root.
$intended = @(
    # Professor-provided starter scripts (compiled, never modified)
    "Assets\Scripts\FittsTargetTelemetryLogger.cs",
    "Assets\Scripts\SpatialVoiceIntentController.cs",
    # Group 05 runtime scripts
    "Assets\Scripts\SpatialTarget.cs",
    "Assets\Scripts\Interaction\DesktopLookController.cs",
    "Assets\Scripts\Interaction\GazeFocusSystem.cs",
    "Assets\Scripts\Interaction\GazeInteractionModel.cs",
    "Assets\Scripts\Interaction\HudText.cs",
    "Assets\Scripts\Interaction\HudTheme.cs",
    "Assets\Scripts\Interaction\XrHudPanel.cs",
    "Assets\Scripts\Telemetry\FittsMath.cs",
    "Assets\Scripts\Telemetry\GazeEventLog.cs",
    "Assets\Scripts\Telemetry\VoiceTelemetryCsvWriter.cs",
    "Assets\Scripts\Telemetry\SessionCsvFile.cs",
    "Assets\Scripts\Telemetry\TrialCsvWriter.cs",
    "Assets\Scripts\Telemetry\TrialRecord.cs",
    "Assets\Scripts\Diagnostics\XRDiagnosticsProbe.cs",
    "Assets\Scripts\Voice\DesktopVoiceInput.cs",
    "Assets\Scripts\Voice\ExternalRecognizerProtocol.cs",
    "Assets\Scripts\Voice\VoiceConfidencePolicy.cs",
    "Assets\Scripts\Voice\VoiceIntentParser.cs",
    # Group 05 Editor-only scripts (wrapped in #if UNITY_EDITOR)
    "Assets\Editor\VoiceVRGazeSelfTests.cs",
    "Assets\Editor\VoiceVRSceneBuilder.cs",
    "Assets\Editor\VoiceVREnvironmentBuilder.cs",
    "Assets\Editor\VoiceVRXRRigBuilder.cs",
    "Assets\Editor\VoiceVRAndroidXRSetup.cs",
    "Assets\Editor\VoiceVRInteractionSdkSetup.cs",
    "Assets\Editor\VoiceVRLabStationsBuilder.cs",
    "Assets\Scripts\Lab\LabPlacementRules.cs",
    "Assets\Scripts\Lab\LabStation.cs",
    "Assets\Scripts\Lab\LabTaskManager.cs",
    "Assets\Scripts\MetaXRButtonHandler.cs",
    "Assets\Editor\CreateMetaAIProviderProfiles.cs",
    "Assets\Editor\IgnoreWebGLProjectSetupTasks.cs",
    "Assets\Editor\MetaXRSceneButtonBuilder.cs",
    "Assets\Editor\MetaXRToolbarButton.cs",
    "Assets\Editor\RemoveUnusedMetaAIBlocks.cs"
)

# --- 1. every intended file must exist ---
$missing = @($intended | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf) })
if ($missing.Count -gt 0) {
    Write-Host "SOURCE LIST ERROR: intended file(s) missing:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" }
    exit 3
}

# --- 2. the allow-list must match what is actually in the two source folders ---
$actual = @()
foreach ($dir in "Assets\Scripts", "Assets\Editor") {
    $p = Join-Path $root $dir
    if (Test-Path -LiteralPath $p) {
        $actual += Get-ChildItem -LiteralPath $p -Recurse -File -Filter "*.cs" | ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\') }
    }
}
$unlisted = @($actual | Where-Object { $intended -notcontains $_ })
if ($unlisted.Count -gt 0) {
    Write-Host "SOURCE LIST ERROR: .cs file(s) on disk are not in the intended list (add them deliberately, do not glob):" -ForegroundColor Red
    $unlisted | ForEach-Object { Write-Host "  $_" }
    exit 3
}
if ($actual.Count -ne $intended.Count) {
    Write-Host ("SOURCE LIST ERROR: intended {0} files but found {1} on disk" -f $intended.Count, $actual.Count) -ForegroundColor Red
    exit 3
}

$sources = $intended | ForEach-Object { Join-Path $root $_ }
Write-Host ("Intended list validated: {0} source files, all present, none unlisted." -f $sources.Count)
Write-Host "Compiling exactly these files:"
$intended | ForEach-Object { Write-Host "  $_" }

$out = Join-Path $env:TEMP "group05_compile_check"
New-Item -ItemType Directory -Force $out | Out-Null
$csc = Join-Path $UnityEditorData "DotNetSdkRoslyn\csc.dll"
$ue = Join-Path $UnityEditorData "Managed\UnityEngine"
$netstd = Join-Path $UnityEditorData "NetStandard\ref\2.1.0\netstandard.dll"

$refs = @("-r:$netstd")
foreach ($f in Get-ChildItem $ue -Filter "UnityEngine*.dll") { $refs += "-r:" + $f.FullName }
foreach ($f in Get-ChildItem $ue -Filter "UnityEditor*.dll") { $refs += "-r:" + $f.FullName }   # Assets/Editor scripts
$netstdDir = Split-Path $netstd
foreach ($f in Get-ChildItem $netstdDir -Filter "*.dll") { if ($f.Name -ne "netstandard.dll") { $refs += "-r:" + $f.FullName } }

# VoiceVRXRRigBuilder.cs uses XR Interaction Toolkit / Input System / XR Core Utils types. Those assemblies only exist after Unity has
# compiled the project once (Library\ScriptAssemblies), so this check needs them and says so instead of failing with 100 type errors.
# VoiceVRAndroidXRSetup.cs additionally uses XR Management and OpenXR (runtime + editor) types; the package assemblies reference the
# mscorlib facade, which Unity ships as a type-forwarding shim.
$refs += "-r:" + (Join-Path $UnityEditorData "NetStandard\compat\2.1.0\shims\netfx\mscorlib.dll")
foreach ($n in "Unity.XR.Interaction.Toolkit", "Unity.InputSystem", "Unity.XR.CoreUtils", "Unity.XR.Management", "Unity.XR.Management.Editor", "Unity.XR.OpenXR", "Unity.XR.OpenXR.Editor", "UnityEngine.UI") {
    $p = Join-Path $root ("Library\ScriptAssemblies\" + $n + ".dll")
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Host ("REFERENCE ERROR: " + $p + " not found. Open the project in Unity once so it compiles the packages, then re-run.") -ForegroundColor Red
        exit 3
    }
    $refs += "-r:" + $p
}

# Note: in the Android configurations the Editor scripts compile to nothing (they are wrapped in #if UNITY_EDITOR).
$configs = @(
    @{ Name = "Windows Editor/Standalone"; Defines = "UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_STANDALONE_WIN;ENABLE_LEGACY_INPUT_MANAGER" },
    @{ Name = "Android (no Windows speech)"; Defines = "UNITY_ANDROID;ENABLE_LEGACY_INPUT_MANAGER" },
    @{ Name = "Android, new Input System only"; Defines = "UNITY_ANDROID" }
)
$failed = 0
$summary = @()
foreach ($c in $configs) {
    $dll = Join-Path $out ("check_" + ($c.Name -replace '[^A-Za-z]', '') + ".dll")
    if (Test-Path -LiteralPath $dll) { Remove-Item -LiteralPath $dll -Force }   # a stale dll must never look like success
    Write-Host ("--- " + $c.Name)
    & dotnet $csc -nologo -target:library -nostdlib+ -langversion:9.0 "-define:$($c.Defines)" -out:$dll @refs @sources
    $code = $LASTEXITCODE
    $built = Test-Path -LiteralPath $dll
    if ($code -ne 0 -or -not $built) { $failed++; $verdict = "FAIL" } else { $verdict = "OK" }
    Write-Host ("RESULT: {0} ({1}) compiler exit code = {2}, output dll produced = {3}" -f $verdict, $c.Name, $code, $built)
    $summary += ("{0,-34} {1,-4} exit={2}" -f $c.Name, $verdict, $code)
}
Write-Host ""
Write-Host ("SUMMARY: {0} source files, {1} configuration(s), {2} failed" -f $sources.Count, $configs.Count, $failed)
$summary | ForEach-Object { Write-Host "  $_" }
exit $failed
