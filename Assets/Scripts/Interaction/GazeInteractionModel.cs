using System;

namespace Group05.VoiceVR
{
    public enum FocusState { None, Focused }
    public enum SelectionState { None, Selected }

    /// <summary>How a command ended. Kept separate from focus and selection state.</summary>
    public enum CommandOutcome { Executed, Rejected, Cancelled, NotImplemented }

    /// <summary>What a successful command changed.</summary>
    public enum CommandAction
    {
        None, SelectedTarget, ClearedSelection, ReportedFocus, ShowedHelp,
        /// <summary>MOVE accepted by the state machine (a target is selected). The caller performs the movement and may still reject it.</summary>
        MoveRequested,
        /// <summary>RELEASE accepted: selection cleared, the target keeps its current position.</summary>
        Released,
        /// <summary>RESET with moved targets: selection cleared; the caller returns every moved target to its start pose.</summary>
        RestoredInitialState,
        /// <summary>GRAB accepted: the target is selected (if it was not already) and now follows the user until it is dropped. The caller performs the following.</summary>
        GrabStarted
    }

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
    /// Manipulation (documented behaviour):
    ///  * SELECT grabs the focused target;
    ///  * MOVE needs a selection (otherwise rejected) and, if a target name is spoken, it must be the selected one;
    ///    the state machine only accepts it, the caller performs and may still reject the physical move;
    ///  * RELEASE needs a selection, commits the target where it is and clears the selection;
    ///  * CANCEL clears the selection (the caller additionally undoes the moves made since SELECT);
    ///  * RESET clears the selection and, if targets were moved, asks the caller to restore every start pose.
    /// </summary>
    public sealed class GazeInteractionModel
    {
        /// <summary>Focus must be held this long before SELECT is accepted (guards against selecting what the gaze just swept over). PROVISIONAL.</summary>
        public float MinFocusSeconds = 0.3f;

        public bool HasFocus { get; private set; }
        public int FocusedId { get; private set; }
        public string FocusedName { get; private set; }
        public float FocusSince { get; private set; }

        /// <summary>True from GRAB until RELEASE / CANCEL / RESET (or the target is lost). The selected target follows the user while this is set.</summary>
        public bool IsCarrying { get; private set; }

        /// <summary>Largest distance one spoken move may ask for. Larger values are rejected, not clamped, so a misheard number is visible.</summary>
        public const float MaxMoveMeters = 10f;

        /// <summary>Called by the caller when carrying cannot start (for example no camera), so the model never claims a carry that is not happening.</summary>
        public void StopCarrying() { IsCarrying = false; }

        public bool HasSelection { get; private set; }
        public int SelectedId { get; private set; }
        public string SelectedName { get; private set; }

        public FocusState Focus { get { return HasFocus ? FocusState.Focused : FocusState.None; } }
        public SelectionState Selection { get { return HasSelection ? SelectionState.Selected : SelectionState.None; } }

        public static readonly string HelpText =
            "Look at a target, hold your gaze, then say 'select that'. Then 'move there' (to where you look) or 'move left/right/up/down/forward/back'. " +
            "'release' (or 'drop it') keeps it where it is; 'cancel' undoes the moves and deselects; 'reset' returns everything to the start. " +
            "'what is this' reports the focused target. " +
            "Add a distance to move by that much ('move left thirty centimeters'); without one a move is 0.5 m. " +
            "'grab this object' picks the target up and it follows you until you say 'drop this object'.";

        /// <summary>Number of targets currently away from their start pose. Maintained by the caller (this model has no positions).</summary>
        public int MovedCount { get; private set; }
        public void SetMovedCount(int count) { MovedCount = Math.Max(0, count); }

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
            if (HasSelection && SelectedId == id) { HasSelection = false; SelectedName = null; IsCarrying = false; selectionCleared = true; }
        }

        public CommandResult Handle(ParsedIntent p, float now)
        {
            if (!p.IsActionable)
                return Reject(p.Kind, "parse_" + p.Status.ToString().ToLowerInvariant(),
                              p.Error ?? "Command not understood.");

            switch (p.Kind)
            {
                case IntentKind.Select: return HandleSelect(p, now);
                case IntentKind.Grab: return HandleGrab(p, now);
                case IntentKind.Cancel: return HandleClear(p.Kind, true);
                case IntentKind.Reset: return HandleReset();
                case IntentKind.Focus:
                    if (!HasFocus) return Reject(p.Kind, "no_focus", "You are not looking at a valid target.");
                    return new CommandResult(CommandOutcome.Executed, CommandAction.ReportedFocus, "",
                        "Looking at: " + FocusedName, p.Kind, true, FocusedId, FocusedName);
                case IntentKind.Help:
                    return new CommandResult(CommandOutcome.Executed, CommandAction.ShowedHelp, "", HelpText, p.Kind, false, 0, null);
                case IntentKind.Move: return HandleMove(p);
                case IntentKind.Release: return HandleRelease();
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

        // GRAB = SELECT (same focus, dwell and name rules) + carry. If the target is already selected it just starts carrying that target.
        private CommandResult HandleGrab(ParsedIntent p, float now)
        {
            if (IsCarrying)
                return Reject(p.Kind, "already_carrying", "'" + SelectedName + "' is already in your hands. Say 'drop this object' first.");
            if (HasSelection)
            {
                IsCarrying = true;
                return new CommandResult(CommandOutcome.Executed, CommandAction.GrabStarted, "",
                    "Grabbed: " + SelectedName + ". It moves with you. Say 'drop this object' to put it down.", p.Kind, true, SelectedId, SelectedName);
            }
            CommandResult sel = HandleSelect(p, now);
            if (sel.Outcome != CommandOutcome.Executed) return sel;
            IsCarrying = true;
            return new CommandResult(CommandOutcome.Executed, CommandAction.GrabStarted, "",
                "Grabbed: " + SelectedName + ". It moves with you. Say 'drop this object' to put it down.", p.Kind, true, SelectedId, SelectedName);
        }

        private CommandResult HandleMove(ParsedIntent p)
        {
            if (!HasSelection)
                return Reject(p.Kind, "nothing_selected", "Nothing is selected. Look at a target and say 'select that' first.");
            if (IsCarrying)
                return Reject(p.Kind, "carrying", "'" + SelectedName + "' is in your hands. Say 'drop this object' before moving it by voice.");
            if (p.HasDistance)
            {
                if (float.IsInfinity(p.DistanceMeters) || p.DistanceMeters <= 0f)
                    return Reject(p.Kind, "invalid_distance", "A move distance must be more than zero.");
                if (p.DistanceMeters > MaxMoveMeters)
                    return Reject(p.Kind, "distance_too_large", "That is too far for one move (largest is " + MaxMoveMeters.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m).");
                if (p.Direction == null)
                    return Reject(p.Kind, "distance_needs_direction", "Say which way: left, right, up, down, forward or back.");
            }
            if (p.TargetName != null &&
                !string.Equals(VoiceIntentParser.Normalize(SelectedName), VoiceIntentParser.Normalize(p.TargetName), StringComparison.Ordinal))
                return Reject(p.Kind, "target_mismatch", "You named '" + p.TargetName + "' but '" + SelectedName + "' is the selected target.");
            return new CommandResult(CommandOutcome.Executed, CommandAction.MoveRequested, "",
                "Moving: " + SelectedName, p.Kind, true, SelectedId, SelectedName);
        }

        private CommandResult HandleRelease()
        {
            if (!HasSelection)
                return Reject(IntentKind.Release, "nothing_selected", "Nothing is selected, so there is nothing to release.");
            string name = SelectedName;
            int id = SelectedId;
            bool wasCarrying = IsCarrying;
            HasSelection = false;
            SelectedName = null;
            IsCarrying = false;
            return new CommandResult(CommandOutcome.Executed, CommandAction.Released, "",
                (wasCarrying ? "Dropped: " : "Released: ") + name + ". It stays where it is.", IntentKind.Release, true, id, name);
        }

        private CommandResult HandleReset()
        {
            IsCarrying = false;
            if (MovedCount <= 0) return HandleClear(IntentKind.Reset, false);   // nothing moved: the original behaviour
            string name = SelectedName;
            int id = SelectedId;
            bool had = HasSelection;
            int moved = MovedCount;
            HasSelection = false;
            SelectedName = null;
            MovedCount = 0;
            return new CommandResult(CommandOutcome.Executed, CommandAction.RestoredInitialState, "",
                "Reset: " + moved + " target(s) returned to the start" + (had ? "; selection cleared." : "."),
                IntentKind.Reset, had, id, name);
        }

        private CommandResult HandleClear(IntentKind kind, bool isCancel)
        {
            if (!HasSelection)
                return Reject(kind, "nothing_selected", "Nothing is selected.");
            string name = SelectedName;
            int id = SelectedId;
            HasSelection = false;
            SelectedName = null;
            IsCarrying = false;
            return new CommandResult(isCancel ? CommandOutcome.Cancelled : CommandOutcome.Executed, CommandAction.ClearedSelection, "",
                (isCancel ? "Cancelled selection of: " : "Selection cleared: ") + name, kind, true, id, name);
        }

        private CommandResult Reject(IntentKind kind, string reason, string message)
        {
            return new CommandResult(CommandOutcome.Rejected, CommandAction.None, reason, message, kind, HasSelection, SelectedId, SelectedName);
        }
    }
}
