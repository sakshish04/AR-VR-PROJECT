using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// The lab task: every item has one designated dock (<see cref="LabStation"/>). Pick an item up (tracked hand, controller, distance grab,
    /// or the voice command "grab this object"), take it to its dock and put it down. When the item is over its own pad, no longer held,
    /// and has stopped moving, it is snapped onto the pad and the dock turns green. Put it on the wrong dock and the dock flashes red and
    /// says where the item belongs. Pick a docked item up again and the dock is free again, so the task can be redone ("reset" sends every item back).
    ///
    /// This component only watches and tidies positions; it never executes a voice command and never changes selection. It does not touch the
    /// telemetry files. Progress is shown in the desktop view (top centre) and in the headset panel (last row of <see cref="XrHudPanel"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public class LabTaskManager : MonoBehaviour
    {
        [Tooltip("An item must be over its pad, not held, and still for this long before it snaps into place.")]
        public float settleSeconds = 0.35f;
        [Tooltip("Slower than this (metres per second) counts as standing still.")]
        public float stillSpeed = 0.05f;
        public float glideSeconds = 0.25f;
        public bool showDesktopHud = true;

        public static LabTaskManager Instance { get; private set; }

        public int Total { get { return stations.Count; } }
        public int PlacedCount { get; private set; }
        public bool Completed { get; private set; }
        public string Message { get { return Time.unscaledTime < messageUntil ? message : ""; } }

        readonly List<LabStation> stations = new List<LabStation>();
        GazeFocusSystem gaze;
        float nextRefresh;
        float startedAt = -1f, completedAt = -1f;
        string message = "";
        float messageUntil;

        // The Interaction SDK's Grabbable is read by reflection so this script does not depend on that package at compile time.
        static Type grabbableType;
        static PropertyInfo selectingCount;
        static bool grabbableLookedUp;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Time.unscaledTime >= nextRefresh) Refresh();
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            bool anyMoved = false;

            foreach (LabStation s in stations)
            {
                SpatialTarget t = s.target;
                if (t == null) continue;
                if (t.HasMovedFromInitial) anyMoved = true;

                Vector3 p = t.ResolvedPosition;
                float speed = (p - s.lastPos).magnitude / dt;
                s.lastPos = p;
                s.stillFor = speed <= stillSpeed ? s.stillFor + dt : 0f;

                bool held = IsHeld(t);
                s.SetHint(held || (gaze != null && (gaze.SelectedTarget == t || gaze.FocusedTarget == t)));

                if (s.IsFilled)
                {
                    // picked up again (held, or carried clearly away): the dock is free again
                    if (held || LabPlacementRules.HasLeftPad(HorizontalDistance(t.GetBounds().center, s.PadTop), s.snapRadius))
                    {
                        s.IsFilled = false;
                        Recount();
                        Say(s.ItemName + " taken off the " + s.stationName + ".", 3f);
                    }
                    continue;
                }

                if (held || s.stillFor < settleSeconds) continue;

                LabStation over = null;
                foreach (LabStation o in stations) if (IsOver(o, t)) { over = o; break; }
                if (over == s) { Place(s); }
                else if (over != null)
                {
                    if (!s.wrongAnnounced)
                    {
                        s.wrongAnnounced = true;
                        over.ShowWrong(1.5f);
                        Say("Not here: the " + s.ItemName + " goes to the " + s.stationName + " (dock " + s.number + " of " + Total + ").", 6f);
                    }
                }
                else s.wrongAnnounced = false;
            }

            if (anyMoved && startedAt < 0f) startedAt = Time.unscaledTime;
        }

        void Refresh()
        {
            nextRefresh = Time.unscaledTime + 1f;
            if (gaze == null) gaze = FindFirstObjectByType<GazeFocusSystem>();
            bool stale = stations.Count == 0;
            for (int i = 0; i < stations.Count && !stale; i++) if (stations[i] == null) stale = true;
            if (!stale) return;
            stations.Clear();
            stations.AddRange(FindObjectsByType<LabStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            stations.RemoveAll(s => s == null || s.target == null);
            stations.Sort((a, b) => a.number.CompareTo(b.number));
            foreach (LabStation s in stations) { s.IsFilled = false; s.lastPos = s.target.ResolvedPosition; s.stillFor = 0f; }
            Recount();
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool IsOver(LabStation s, SpatialTarget t)
        {
            Bounds b = t.GetBounds();
            return LabPlacementRules.IsOverPad(HorizontalDistance(b.center, s.PadTop), b.min.y - s.PadTop.y, s.snapRadius);
        }

        bool IsHeld(SpatialTarget t)
        {
            if (gaze != null && gaze.Model.IsCarrying && gaze.SelectedTarget == t) return true;   // voice grab
            if (!grabbableLookedUp)
            {
                grabbableLookedUp = true;
                foreach (Component c in t.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "Grabbable")
                    {
                        grabbableType = c.GetType();
                        selectingCount = grabbableType.GetProperty("SelectingPointsCount", BindingFlags.Public | BindingFlags.Instance);
                        break;
                    }
            }
            if (grabbableType == null || selectingCount == null) return false;
            Component g = t.GetComponent(grabbableType);
            if (g == null) return false;
            try { return (int)selectingCount.GetValue(g, null) > 0; }   // a hand, controller or distance grab is holding it
            catch (Exception) { return false; }
        }

        void Place(LabStation s)
        {
            s.IsFilled = true;
            s.wrongAnnounced = false;
            s.target.GlideTo(s.SnapPositionFor(s.target), glideSeconds);
            Recount();
            Say("DOCKED: " + s.ItemName + " in the " + s.stationName + "  (" + LabPlacementRules.Progress(PlacedCount, Total) + ")", 5f);
            UnityEngine.Debug.Log("[LabTask] " + s.ItemName + " docked in " + s.stationName + " (" + PlacedCount + "/" + Total + ")");
            if (Completed)
            {
                completedAt = Time.unscaledTime;
                string took = LabPlacementRules.FormatDuration(startedAt < 0f ? 0f : completedAt - startedAt);
                Say("LAB COMPLETE: all " + Total + " items docked in " + took + ".", 30f);
                UnityEngine.Debug.Log("[LabTask] complete in " + took);
            }
        }

        void Recount()
        {
            int n = 0;
            foreach (LabStation s in stations) if (s.IsFilled) n++;
            PlacedCount = n;
            bool done = Total > 0 && n == Total;
            if (!done) completedAt = -1f;
            Completed = done;
        }

        void Say(string text, float seconds)
        {
            message = text;
            messageUntil = Time.unscaledTime + seconds;
        }

        LabStation StationFor(SpatialTarget t)
        {
            if (t == null) return null;
            foreach (LabStation s in stations) if (s.target == t) return s;
            return null;
        }

        /// <summary>One line for the headset panel and the desktop HUD: the latest event, otherwise where the looked-at / selected item belongs.</summary>
        public string StatusLine()
        {
            if (Completed)
                return "LAB COMPLETE: all " + Total + " items docked in " + LabPlacementRules.FormatDuration(startedAt < 0f ? 0f : completedAt - startedAt);
            string m = Message;
            if (m.Length > 0) return m;
            SpatialTarget t = null;
            if (gaze != null) t = gaze.SelectedTarget != null ? gaze.SelectedTarget : gaze.FocusedTarget;
            LabStation s = StationFor(t);
            if (s != null)
                return s.ItemName + " goes to the " + s.stationName + " (dock " + s.number + " of " + Total + (s.IsFilled ? ", already docked)" : ")");
            return "Take each item to its own dock (" + LabPlacementRules.Progress(PlacedCount, Total) + " docked)";
        }

        void OnGUI()
        {
            if (!showDesktopHud || Total == 0) return;
            Vector2 v = HudTheme.Begin();
            float w = Mathf.Min(560f, v.x - 2f * HudTheme.EdgeMargin);
            var r = new Rect((v.x - w) * 0.5f, HudTheme.ResultTop, w, 54f);
            HudTheme.Panel(r, HudTheme.PanelFill, Completed ? HudTheme.Success : HudTheme.PanelBorder);
            HudTheme.Label(new Rect(r.x + 14f, r.y + 6f, w - 28f, 22f), "LAB TASK   " + LabPlacementRules.Progress(PlacedCount, Total) + " docked",
                           HudTheme.FontLead, Completed ? HudTheme.Success : HudTheme.Cyan, true, TextAnchor.MiddleLeft);
            HudTheme.Label(new Rect(r.x + 14f, r.y + 28f, w - 28f, 22f), HudTheme.Fit(StatusLine(), Mathf.Max(20, Mathf.FloorToInt((w - 28f) / 7.2f))),
                           HudTheme.FontBody, HudTheme.Text, false, TextAnchor.MiddleLeft);
            HudTheme.End();
        }
    }
}
