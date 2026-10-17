using System;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Appends validated <see cref="TrialRecord"/> rows to a session-stamped CSV that is never overwritten
    /// (file handling lives in <see cref="SessionCsvFile"/>). Failures are reported through the return value
    /// and <see cref="LastError"/>; this class never throws. Pure C# (no Unity dependency).
    /// </summary>
    public sealed class TrialCsvWriter
    {
        private readonly SessionCsvFile file;
        private readonly DataOrigin origin;

        public string LastError { get; private set; }
        public int RowsWritten { get { return file.RowsWritten; } }
        public string FilePath { get { return file.FilePath; } }

        public TrialCsvWriter(string folder, string sessionId, DataOrigin origin)
        {
            this.origin = origin;
            file = new SessionCsvFile(folder, "trials", sessionId, origin, TrialRecord.CsvHeader);
        }

        public static string NewSessionId(DateTime utcNow) { return SessionCsvFile.NewSessionId(utcNow); }
        public static string SanitizeToken(string s) { return SessionCsvFile.SanitizeToken(s); }

        public bool TryWrite(TrialRecord record)
        {
            LastError = null;
            try
            {
                if (record == null) { LastError = "record is null"; return false; }
                if (record.Origin != origin) { LastError = "record data_origin does not match this writer's origin"; return false; }
                var errors = record.Validate();
                if (errors.Count > 0) { LastError = "invalid record: " + string.Join("; ", errors.ToArray()); return false; }
                if (file.TryAppend(record.ToCsvRow())) return true;
                LastError = file.LastError;
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
