// Speech helper for VoiceVR using the Windows Runtime speech recognizer (Windows.Media.SpeechRecognition), in a separate window-less process.
// It is the recognizer Unity's own KeywordRecognizer uses on Windows (Unity's High/Medium/Low/Rejected levels are this API's), which recognised the
// developers' voices far better than the older System.Speech desktop engine, and being a separate process it does not depend on which window has focus.
//
// Built on demand by DesktopVoiceInput with the .NET Framework compiler that ships with Windows (nothing to install):
//   csc /r:Windows.Media.winmd /r:Windows.Foundation.winmd /r:Windows.Globalization.winmd /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.dll
//
// Arguments:  -GrammarFile <file with one phrase per line>  -ParentPid <unity process id>
// Output (one line per event, UTF-8), same protocol as SystemSpeechHelper.ps1 (see Assets/Scripts/Voice/ExternalRecognizerProtocol.cs):
//   READY|<language>     PHRASE|<conf>|<speech end UTC>|<text>  (High 0.90, Medium 0.35, Low 0.10)     HEARD|rejected|<conf>|<text>
//   HYP|<conf>|<text>    PROBLEM|<reason>     ERROR|<message>
// No audio is recorded or saved; only the matched grammar phrase is printed. It exits when the parent process exits.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.Media.SpeechRecognition;

public static class WinRtSpeechHelper
{
    static readonly object Gate = new object();
    static volatile bool restartNeeded;

    static void Write(string line)
    {
        lock (Gate) { Console.Out.WriteLine(line); Console.Out.Flush(); }
    }

    // Plain polling instead of AsTask(): the compiler can only see the split Windows .winmd files here, where the AsTask extension methods do not bind.
    static T Wait<T>(IAsyncOperation<T> op)
    {
        while (op.Status == AsyncStatus.Started) Thread.Sleep(10);
        if (op.Status == AsyncStatus.Error) throw new InvalidOperationException("operation failed (0x" + ((uint)op.ErrorCode.HResult).ToString("X8") + ")");
        return op.GetResults();
    }

    static void Wait(IAsyncAction op)
    {
        while (op.Status == AsyncStatus.Started) Thread.Sleep(10);
        if (op.Status == AsyncStatus.Error) throw new InvalidOperationException("operation failed (0x" + ((uint)op.ErrorCode.HResult).ToString("X8") + ")");
        op.GetResults();
    }

    static string Conf(double c) { return c.ToString("0.000", CultureInfo.InvariantCulture); }

    static bool Alive(int pid)
    {
        try { Process.GetProcessById(pid); return true; } catch (Exception) { return false; }
    }

    public static int Main(string[] args)
    {
        string grammarFile = null; int parentPid = 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-GrammarFile") grammarFile = args[i + 1];
            else if (args[i] == "-ParentPid") int.TryParse(args[i + 1], out parentPid);
        }
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (grammarFile == null) throw new ArgumentException("missing -GrammarFile");
            string[] phrases = File.ReadAllLines(grammarFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (phrases.Length == 0) throw new InvalidOperationException("grammar file is empty");

            var rec = new SpeechRecognizer();
            rec.Constraints.Add(new SpeechRecognitionListConstraint(phrases));
            var comp = Wait(rec.CompileConstraintsAsync());
            if (comp.Status != SpeechRecognitionResultStatus.Success) throw new InvalidOperationException("could not compile the phrase list: " + comp.Status);

            var session = rec.ContinuousRecognitionSession;
            try { session.AutoStopSilenceTimeout = TimeSpan.FromMinutes(30); } catch (Exception) { }
            session.ResultGenerated += new TypedEventHandler<SpeechContinuousRecognitionSession, SpeechContinuousRecognitionResultGeneratedEventArgs>((s, a) =>
            {
                try
                {
                    var r = a.Result;
                    string text = r.Text ?? "";
                    double c = r.Confidence == SpeechRecognitionConfidence.High ? 0.90 : r.Confidence == SpeechRecognitionConfidence.Medium ? 0.35 : r.Confidence == SpeechRecognitionConfidence.Low ? 0.10 : 0.0;
                    if (r.Confidence == SpeechRecognitionConfidence.Rejected || text.Length == 0) { Write("HEARD|rejected|" + Conf(c) + "|" + text); return; }
                    string end = "";
                    try { end = (r.PhraseStartTime + r.PhraseDuration).UtcDateTime.ToString("o", CultureInfo.InvariantCulture); } catch (Exception) { }
                    Write("PHRASE|" + Conf(c) + "|" + end + "|" + text);
                }
                catch (Exception ex) { Write("ERROR|" + ex.Message); }
            });
            session.Completed += new TypedEventHandler<SpeechContinuousRecognitionSession, SpeechContinuousRecognitionCompletedEventArgs>((s, a) =>
            {
                if (a.Status == SpeechRecognitionResultStatus.MicrophoneUnavailable) Write("PROBLEM|NoSignal");
                else if (a.Status == SpeechRecognitionResultStatus.AudioQualityFailure) Write("PROBLEM|TooNoisy");
                restartNeeded = true;
            });
            rec.HypothesisGenerated += new TypedEventHandler<SpeechRecognizer, SpeechRecognitionHypothesisGeneratedEventArgs>((s, a) => { try { Write("HYP|0.000|" + a.Hypothesis.Text); } catch (Exception) { } });

            Wait(session.StartAsync());
            Write("READY|" + rec.CurrentLanguage.LanguageTag);

            while (true)
            {
                Thread.Sleep(300);
                if (parentPid > 0 && !Alive(parentPid)) break;
                if (restartNeeded)
                {
                    restartNeeded = false;
                    try { Wait(session.StartAsync()); }
                    catch (Exception ex) { Write("PROBLEM|" + ex.GetType().Name); Thread.Sleep(1000); restartNeeded = true; }
                }
            }
            try { Wait(session.StopAsync()); } catch (Exception) { }
            return 0;
        }
        catch (Exception ex)
        {
            Exception e = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
            Write("ERROR|" + e.Message);
            return 1;
        }
    }
}
