# Speech helper for VoiceVR (Windows PowerShell 5.1, System.Speech - part of Windows, nothing to install).
# Listens on the DEFAULT microphone for the fixed phrases in a grammar file and prints one line per result to stdout:
#   READY|<engine id>
#   PHRASE|<confidence 0..1>|<speech end, UTC ISO-8601>|<text>
#   HEARD|rejected|<confidence 0..1>|<best guess text, may be empty>   (speech detected but not matched; never executed)
#   ERROR|<message>
# It exists because Unity's own keyword recognizer only delivered phrases while the Unity window had focus (see
# Assets/Scripts/Voice/ExternalRecognizerProtocol.cs). This process does not depend on window focus.
# No audio is recorded or saved; only the matched grammar phrase is printed. It exits when the parent Unity process exits.
#
# The recognizer is driven by a small compiled C# class (real .NET event handlers). An earlier version used PowerShell
# Register-ObjectEvent actions, which never fired while the script was waiting, so no phrase was ever reported.
param(
    [Parameter(Mandatory = $true)][string]$GrammarFile,
    [int]$ParentPid = 0,
    [string]$Culture = 'en-US'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$src = @'
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Threading;

public static class VoiceHelperMain
{
    static readonly object Gate = new object();

    static void Write(string line)
    {
        lock (Gate) { Console.Out.WriteLine(line); Console.Out.Flush(); }
    }

    static string Conf(float c)
    {
        return c.ToString("0.000", CultureInfo.InvariantCulture);
    }

    static bool Alive(int pid)
    {
        try { Process.GetProcessById(pid); return true; } catch (Exception) { return false; }
    }

    public static int Run(string grammarFile, int parentPid, string culture)
    {
        try
        {
            string[] phrases = File.ReadAllLines(grammarFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (phrases.Length == 0) throw new InvalidOperationException("grammar file is empty: " + grammarFile);
            var ci = new CultureInfo(culture);
            using (var engine = new SpeechRecognitionEngine(ci))
            {
                var gb = new GrammarBuilder { Culture = ci };
                gb.Append(new Choices(phrases));
                engine.LoadGrammar(new Grammar(gb));
                engine.SetInputToDefaultAudioDevice();
                // Accept more candidates: the default rejection threshold discards quiet or accented speech without a trace. A weak match is still
                // reported (as a Low confidence PHRASE) and Unity's own confidence floor then decides, visibly, whether it is executed.
                try { engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 10); } catch (Exception) { }

                engine.SpeechRecognized += (s, a) =>
                {
                    try
                    {
                        string end = "";
                        if (a.Result.Audio != null) end = (a.Result.Audio.StartTime + a.Result.Audio.Duration).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                        Write("PHRASE|" + Conf(a.Result.Confidence) + "|" + end + "|" + a.Result.Text);
                    }
                    catch (Exception ex) { Write("ERROR|" + ex.Message); }
                };
                engine.SpeechRecognitionRejected += (s, a) =>
                {
                    try
                    {
                        string t = a.Result != null ? a.Result.Text : "";
                        float c = a.Result != null ? a.Result.Confidence : 0f;
                        Write("HEARD|rejected|" + Conf(c) + "|" + t);
                    }
                    catch (Exception) { }
                };

                engine.RecognizeAsync(RecognizeMode.Multiple);
                Write("READY|" + engine.RecognizerInfo.Id);
                while (true)
                {
                    Thread.Sleep(300);
                    if (parentPid > 0 && !Alive(parentPid)) break;
                }
                engine.RecognizeAsyncCancel();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Write("ERROR|" + ex.Message);
            return 1;
        }
    }
}
'@

try {
    Add-Type -AssemblyName System.Speech
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Speech, System.Core
    exit ([VoiceHelperMain]::Run($GrammarFile, $ParentPid, $Culture))
} catch {
    [Console]::Out.WriteLine('ERROR|' + $_.Exception.Message)
    [Console]::Out.Flush()
    exit 1
}
