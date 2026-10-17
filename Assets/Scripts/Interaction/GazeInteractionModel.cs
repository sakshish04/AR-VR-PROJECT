using System;

namespace Group05.VoiceVR
{
    public enum FocusState { None, Focused }
    public enum SelectionState { None, Selected }

    /// <summary>How a command ended. Kept separate from focus and selection state.</summary>
    public enum CommandOutcome { Executed, Rejected, Cancelled, NotImplemented }

    /// <summary>What a successful command changed.</summary>
    public enum CommandAction { None, SelectedTarget, ClearedSelection, ReportedFocus, ShowedHelp }

    public readonly struct CommandResult
    {
        public readonly CommandOutcome Outcome;
        public readonly CommandAction Action;
        /// <summary>Short machine-readable reason (e.g. "no_focus"); empty on success.</summary>
        public readonly string Reason;
        public readonly string Message;
        public readonly IntentKind Intent;
        public readonly bool HasTarget;
        public readonly int TargetId;
        public readonly string TargetName;

        public CommandResult(CommandOutcome outcome, CommandAction action, string reason, string message,
                             IntentKind intent, bool hasTarget, int targetId, string targetName)
        {
            Outcome = outcome;
            Action = action;
            Reason = reason ?? string.Empty;
            Message = message ?? string.Empty;
            Intent = intent;
            HasTarget = hasTarget;
            TargetId = targetId;
            TargetName = targetName;
        }

        public override string ToString() { return Outcome + "/" + Action + (Reason.Length > 0 ? " (" + Reason + ")" : ""); }
    }

    /// <summary>
    /// Pure-C# focus / selection / command state for the gaze loop (no Unity dependency, so it is unit-testable).
    ///
    /// Four things are kept separate:
    ///  * focus: which valid target the gaze ray currently rests on (never executes anything);
    ///  * selection: a target the user explicitly confirmed with a SELECT command;
    ///  * command outcome: Executed / Rejected / Cancelled / NotImplemented, with a reason;
    ///  * input modality, which the caller logs (this model does not care where text came from).
    ///
    /// Confirmation rule: a target becomes selected only when ALL hold: the utterance parsed as an actionable SELECT
    /// (not negated, not ambiguous), a valid target has been focused for at least <see cref="MinFocusSeconds"/>,
    /// nothing is selected yet, and any spoken target name matches the focused target. Focus alone never selects.
    ///
    /// MOVE and RELEASE are deliberately NOT implemented in Phase 3: they return NotImplemented and change nothing.
    /// </summary>
    public sealed class GazeInteractionModel
    {
        /// <summary>Focus must be held this long before SELECT is accepted (guards against selecting what the gaze just swept over). PROVISIONAL.</summary>
        public float MinFocusSeconds = 0.3f;

        public bool HasFocus { get; private set; }
        public int FocusedId { get; private set; }
        public string FocusedName { get; private set; }
        public float FocusSince { get; private set; }

        public bool HasSelection { get; private set; }
        public int SelectedId { get; private set; }
        public string SelectedName { get; private set; }

        public FocusState Focus { get { return HasFocus ? FocusState.Focused : FocusState.None; } }
        public SelectionState Selection { get { return HasSelection ? SelectionState.Selected : SelectionState.None; } }

        public static readonly string HelpText =
            "Look at a target, hold your gaze, then say 'select that'. 'cancel' or 'reset' clears the selection. " +
            "'what is this' reports the focused target. MOVE and RELEASE are not available yet.";

        public float FocusAge(float now) { return HasFocus ? Math.Max(0f, now - FocusSince) : 0f; }

        /// <summary>
        /// Updates what the gaze rests on. Pass hasTarget=false when the ray hits nothing or something that is not a valid
        /// target. Returns true if the focused target changed (entered, left or switched).
        /// </summary>
        public bool SetFocus(bool hasTarget, int id, string name, float now)
        {
            if (!hasTarget)
            {
                if (!HasFocus) return false;
                HasFocus = false;
                FocusedName = null;
                return true;
            }
            if (HasFocus && FocusedId == id) return false;
            HasFocus = true;
            FocusedId = id;
            FocusedName = name ?? string.Empty;
            FocusSince = now;
            return true;
        }

        /// <summary>Call when a focused or selected target no longer exists. Returns which states were cleared.</summary>
        public void TargetLost(int id, out bool focusCleared, out bool selectionCleared)
        {
            focusCleared = false;
            selectionCleared = false;
            if (HasFocus && FocusedId == id) { HasFocus = false; FocusedName = null; focusCleared = true; }
            if (HasSelection && SelectedId == id) { HasSelection = false; SelectedName = null; selectionCleared = true; }
        }

        public CommandResult Handle(ParsedIntent p, float now)
        {
            if (!p.IsActionable)
                return Reject(p.Kind, "parse_" + p.Status.ToString().ToLowerInvariant(),
                              p.Error ?? "Command not understood.");

            switch (p.Kind)
            {
                case IntentKind.Select: return HandleSelect(p, now);
                case IntentKind.Cancel: return HandleClear(p.Kind, true);
                case IntentKind.Reset: return HandleClear(p.Kind, false);
                case IntentKind.Focus:
                    if (!HasFocus) return Reject(p.Kind, "no_focus", "You are not looking at a valid target.");
                    return new CommandResult(CommandOutcome.Executed, CommandAction.ReportedFocus, "",
                        "Looking at: " + FocusedName, p.Kind, true, FocusedId, FocusedName);
                case IntentKind.Help:
                    return new CommandResult(CommandOutcome.Executed, CommandAction.ShowedHelp, "", HelpText, p.Kind, false, 0, null);
                case IntentKind.Move:
                case IntentKind.Release:
                    return new CommandResult(CommandOutcome.NotImplemented, CommandAction.None, "not_implemented_phase3",
                        p.Kind + " is not available in this phase. Nothing was changed.", p.Kind, HasSelection, SelectedId, SelectedName);
                default:
                    return Reject(p.Kind, "unsupported_intent", "Unsupported command.");
            }
        }

        private CommandResult HandleSelect(ParsedIntent p, float now)
        {
            if (HasSelection)
                return Reject(p.Kind, "already_selected", "'" + SelectedName + "' is already selected. Say 'cancel' first.");
            if (!HasFocus)
                return Reject(p.Kind, "no_focus", "You are not looking at a valid target.");
            if (FocusAge(now) < MinFocusSeconds)
                return Reject(p.Kind, "focus_too_recent", "Hold your gaze on the target a moment longer, then try again.");
            if (p.TargetName != null &&
                !string.Equals(VoiceIntentParser.Normalize(FocusedName), VoiceIntentParser.Normalize(p.TargetName), StringComparison.Ordinal))
                return Reject(p.Kind, "target_mismatch", "You named '" + p.TargetName + "' but are looking at '" + FocusedName + "'.");

            HasSelection = true;
            SelectedId = FocusedId;
            SelectedName = FocusedName;
            return new CommandResult(CommandOutcome.Executed, CommandAction.SelectedTarget, "",
                "Selected: " + SelectedName, p.Kind, true, SelectedId, SelectedName);
        }

        private CommandResult HandleClear(IntentKind kind, bool isCancel)
        {
            if (!HasSelection)
                return Reject(kind, "nothing_selected", "Nothing is selected.");
            string name = SelectedName;
            int id = SelectedId;
            HasSelection = false;
            SelectedName = null;
            return new CommandResult(isCancel ? CommandOutcome.Cancelled : CommandOutcome.Executed, CommandAction.ClearedSelection, "",
                (isCancel ? "Cancelled selection of: " : "Selection cleared: ") + name, kind, true, id, name);
        }

        private CommandResult Reject(IntentKind kind, string reason, string message)
        {
            return new CommandResult(CommandOutcome.Rejected, CommandAction.None, reason, message, kind, HasSelection, SelectedId, SelectedName);
        }
    }
}
