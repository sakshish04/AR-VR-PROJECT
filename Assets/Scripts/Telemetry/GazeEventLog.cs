using System;
using System.Collections.Generic;
using System.Globalization;

namespace Group05.VoiceVR
{
    /// <summary>
    /// One gaze/command event row. This is a SEPARATE, PROPOSED event schema (PENDING CONFIRMATION); it does not
    /// change the canonical per-trial schema in <see cref="TrialRecord"/>. It carries no participant identifier,
    /// so Real data is refused until a participant link is designed.
    /// </summary>
    public class GazeEventRecord
    {
        public const string SchemaVersion = "0.1-proposed-events";

        public const string CsvHeader =
            "schema_version,data_origin,session_id,event_index,realtime_s,utc,event_type,target_name,input_modality,intent,outcome,reason";
        public const int ColumnCount = 12;

        // event_type values
        public const string FocusEnter = "focus_enter";
        public const string FocusExit = "focus_exit";
        public const string Command = "command";
        public const string TargetLost = "target_lost";
        public const string CameraMissing = "camera_missing";

        public DataOrigin Origin;
        public string SessionId;
        public int EventIndex;
        public float RealtimeS;
        public DateTime Utc;
        public string EventType;
        public string TargetName;
        /// <summary>Only set for command events.</summary>
        public InputModality? Modality;
        public string Intent;
        public string Outcome;
        public string Reason;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Origin == DataOrigin.Real) errors.Add("Real data is not allowed in the event log until a participant link is designed");
            if (string.IsNullOrEmpty(SessionId) || SessionCsvFile.SanitizeToken(SessionId) != SessionId)
                errors.Add("session_id must be non-empty and contain only letters, digits, '_' or '-'");
            if (EventIndex < 1) errors.Add("event_index must be >= 1");
            if (string.IsNullOrEmpty(EventType)) errors.Add("event_type is required");
            return errors;
        }

        public string ToCsvRow()
        {
            return FittsMath.CsvRow(
                SchemaVersion,
                Origin.ToString().ToLowerInvariant(),
                SessionId,
                EventIndex.ToString(CultureInfo.InvariantCulture),
                FittsMath.F(RealtimeS, 3),
                Utc.ToString("o", CultureInfo.InvariantCulture),
                EventType,
                TargetName,
                Modality.HasValue ? Modality.Value.ToString() : string.Empty,
                Intent,
                Outcome,
                Reason);
        }
    }

    /// <summary>Never-overwrite event log writer. Never throws; see <see cref="SessionCsvFile"/>.</summary>
    public sealed class GazeEventCsvWriter
    {
        private readonly SessionCsvFile file;
        private readonly DataOrigin origin;
        public string LastError { get; private set; }
        public int RowsWritten { get { return file.RowsWritten; } }
        public string FilePath { get { return file.FilePath; } }

        public GazeEventCsvWriter(string folder, string sessionId, DataOrigin origin)
        {
            this.origin = origin;
            file = new SessionCsvFile(folder, "gaze_events", sessionId, origin, GazeEventRecord.CsvHeader);
        }

        public bool TryWrite(GazeEventRecord record)
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
