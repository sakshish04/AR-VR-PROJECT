# Compiles and runs the Unity-independent tests using the Roslyn compiler and runtime bundled with the Unity Editor.
# Safe to run while the Unity Editor is open (it never touches the project's Library). Output goes to $env:TEMP.
param([string]$UnityEditorData = "C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data")

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP "group05_standalone_tests"
New-Item -ItemType Directory -Force $out | Out-Null

$csc = Join-Path $UnityEditorData "DotNetSdkRoslyn\csc.dll"
$ref = Get-ChildItem (Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.NETCore.App") -Directory | Sort-Object Name | Select-Object -Last 1
$names = "System.Private.CoreLib.dll","System.Runtime.dll","System.Console.dll","System.Collections.dll","System.Linq.dll","System.Threading.dll","System.Globalization.dll","System.Runtime.Extensions.dll","System.Memory.dll","System.Text.RegularExpressions.dll","System.IO.FileSystem.dll","System.Text.Encoding.Extensions.dll"
$refs = $names | ForEach-Object { "-r:" + (Join-Path $ref.FullName $_) }

$dll = Join-Path $out "StandaloneTests.dll"
& dotnet $csc -nologo -target:exe -out:$dll -langversion:latest @refs `
    (Join-Path $PSScriptRoot "StandaloneTests.cs") `
    (Join-Path $root "Assets\Scripts\Voice\VoiceIntentParser.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\FittsMath.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\TrialRecord.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\TrialCsvWriter.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\SessionCsvFile.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\GazeEventLog.cs") `
    (Join-Path $root "Assets\Scripts\Telemetry\VoiceTelemetryCsvWriter.cs") `
    (Join-Path $root "Assets\Scripts\Interaction\GazeInteractionModel.cs") `
    (Join-Path $root "Assets\Scripts\Interaction\HudText.cs") `
    (Join-Path $root "Assets\Scripts\Lab\LabPlacementRules.cs") `
    (Join-Path $root "Assets\Scripts\Trials\FittsTrialPlan.cs") `
    (Join-Path $root "Assets\Scripts\Trials\TrialEngine.cs") `
    (Join-Path $root "Assets\Scripts\Voice\ExternalRecognizerProtocol.cs") `
    (Join-Path $root "Assets\Scripts\Voice\VoiceConfidencePolicy.cs")
if ($LASTEXITCODE -ne 0) { Write-Error "Compile failed"; exit 2 }

$ver = $ref.Name
Set-Content -Path (Join-Path $out "StandaloneTests.runtimeconfig.json") -Value ('{"runtimeOptions":{"tfm":"net' + $ver.Split('.')[0] + '.0","framework":{"name":"Microsoft.NETCore.App","version":"' + $ver + '"}}}')
& dotnet $dll
exit $LASTEXITCODE
