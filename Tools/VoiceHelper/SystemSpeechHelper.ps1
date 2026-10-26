# Speech helper for VoiceVR (Windows PowerShell 5.1, System.Speech - part of Windows, nothing to install).
# Listens on the DEFAULT microphone for the fixed phrases in a grammar file and prints one line per result to stdout:
#   READY|<engine id>
#   PHRASE|<confidence 0..1>|<speech end, UTC ISO-8601>|<text>
#   HEARD|rejected|<confidence 0..1>|<best guess text, may be empty>   (speech detected but not matched; never executed)
#   HYP|<confidence>|<text>   (what the engine currently thinks it hears, before it has finished; diagnostics only, never executed)
#   PROBLEM|<TooQuiet|TooLoud|TooNoisy|TooFast|TooSlow|NoSignal|...>   (the engine says why the audio could not be understood)
#   ERROR|<message>
# It exists because Unity's own keyword recognizer only delivered phrases while the Unity window had focus (see
# Assets/Scripts/Voice/ExternalRecognizerProtocol.cs). This process does not depend on window focus.
# In 'stdin' mode Unity's own microphone capture (which was measured to deliver a clean signal) feeds the engine, so the Windows legacy capture path is not used.
# No audio is recorded or saved; only the matched grammar phrase is printed. It exits when the parent Unity process exits.
#
# The recognizer is driven by a small compiled C# class (real .NET event handlers). An earlier version used PowerShell
# Register-ObjectEvent actions, which never fired while the script was waiting, so no phrase was ever reported.
param(
    [Parameter(Mandatory = $true)][string]$GrammarFile,
    [int]$ParentPid = 0,
    [string]$Culture = 'en-US',
    # 'default' = the engine opens the Windows default microphone itself; 'stdin' = Unity captures the microphone and sends raw 16 kHz 16-bit mono PCM on stdin
    [ValidateSet('default', 'stdin')][string]$AudioSource = 'default'
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

// System.Speech insists on a seekable stream; a live pipe is not. This wrapper reads the pipe and pretends to seek (the position just counts bytes read).
public sealed class LivePcmStream : Stream
{
    readonly Stream src; long pos;
    public LivePcmStream(Stream source) { src = source; }
    public override bool CanRead { get { return true; } }
    public override bool CanSeek { get { return true; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { return long.MaxValue; } }
    public override long Position { get { return pos; } set { } }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = src.Read(buffer, offset, count);
        pos += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) { return pos; }
    public override void Flush() { }
    public override void SetLength(long value) { throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
}

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

    public static int Run(string grammarFile, int parentPid, string culture, string input)
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
                if (input == "stdin")
                    engine.SetInputToAudioStream(new LivePcmStream(Console.OpenStandardInput()), new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));
                else
                    engine.SetInputToDefaultAudioDevice();
                // Accept more candidates: the default rejection threshold discards quiet or accented speech without a trace. A weak match is still
                // reported (as a Low confidence PHRASE) and Unity's own confidence floor then decides, visibly, whether it is executed.
                try { engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 0); } catch (Exception) { }
                // End of utterance: with background noise the engine can wait a long time for 'silence' and then reports nothing for a whole sentence.
                // Finish an utterance after a short pause, and give up on continuous noise after a few seconds so it is judged instead of waiting forever.
                try { engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(350); } catch (Exception) { }
                try { engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(600); } catch (Exception) { }
                try { engine.BabbleTimeout = TimeSpan.FromSeconds(3); } catch (Exception) { }
                engine.SpeechHypothesized += (s, a) =>
                {
                    try { Write("HYP|" + Conf(a.Result.Confidence) + "|" + a.Result.Text); } catch (Exception) { }
                };

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
                engine.AudioSignalProblemOccurred += (s, a) =>
                {
                    try { Write("PROBLEM|" + a.AudioSignalProblem.ToString()); } catch (Exception) { }
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
    exit ([VoiceHelperMain]::Run($GrammarFile, $ParentPid, $Culture, $AudioSource))
} catch {
    [Console]::Out.WriteLine('ERROR|' + $_.Exception.Message)
    [Console]::Out.Flush()
    exit 1
}
