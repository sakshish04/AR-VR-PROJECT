using System;
using System.IO;
using System.Text;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Append-only, never-overwrite CSV file for one logging session. The first write claims a brand-new file
    /// (FileMode.CreateNew; "_2", "_3"... on collision) and writes the header; later writes append.
    /// The file name is built only from sanitized tokens so caller strings cannot escape the folder.
    /// Never throws: failures are reported through the return value and <see cref="LastError"/>.
    /// Pure C# (no Unity dependency).
    /// </summary>
    public sealed class SessionCsvFile
    {
        private readonly string folder;
        private readonly string baseName;
        private readonly string header;
        private string filePath;

        public string LastError { get; private set; }
        public int RowsWritten { get; private set; }
        /// <summary>Full path once the first row is written; before that, the intended path (display only).</summary>
        public string FilePath { get { return filePath; } }

        public SessionCsvFile(string folder, string filePrefix, string sessionId, DataOrigin origin, string header)
        {
            this.folder = folder;
            this.header = header;
            baseName = SanitizeToken(filePrefix) + "_" + origin.ToString().ToLowerInvariant() + "_" + SanitizeToken(sessionId);
            filePath = folder == null ? null : Path.Combine(folder, baseName + ".csv");
        }

        public static string NewSessionId(DateTime utcNow)
        {
            return utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Keeps only [A-Za-z0-9_-], capped at 40 characters; returns "x" when nothing is left.</summary>
        public static string SanitizeToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (sb.Length >= 40) break;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.Length == 0 ? "x" : sb.ToString();
        }

        public bool TryAppend(string row)
        {
            LastError = null;
            try
            {
                if (string.IsNullOrEmpty(folder)) { LastError = "output folder is not set"; return false; }
                if (row == null) { LastError = "row is null"; return false; }

                string fullFolder = Path.GetFullPath(folder);
                Directory.CreateDirectory(fullFolder);

                if (RowsWritten == 0)
                {
                    for (int n = 1; n <= 99; n++)
                    {
                        string candidate = Path.GetFullPath(Path.Combine(fullFolder, baseName + (n == 1 ? "" : "_" + n) + ".csv"));
                        if (!candidate.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase)) { LastError = "path escaped output folder"; return false; }
                        try
                        {
                            using (var fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                            {
                                w.WriteLine(header);
                                w.WriteLine(row);
                            }
                            filePath = candidate;
                            RowsWritten = 1;
                            return true;
                        }
                        catch (IOException) when (File.Exists(candidate)) { /* name taken: try the next suffix */ }
                    }
                    LastError = "could not find a free file name";
                    return false;
                }

                using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                    w.WriteLine(row);
                RowsWritten++;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
