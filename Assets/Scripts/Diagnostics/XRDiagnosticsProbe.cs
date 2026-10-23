using System.Collections.Generic;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Read-only XR diagnostics for Editor / Development builds. It creates itself in Play Mode (no scene change), changes nothing
    /// and writes only to the Console. Press F10 for an immediate report; otherwise it reports every few seconds, but only when
    /// something changed. Output lines start with "[XRProbe]".
    ///
    /// It separates the failure categories: loader/session, display, head pose, controller pose, controller buttons, ray interactors,
    /// and which camera the gaze system uses. A "session FOCUSED" log line proves none of these; this does.
    /// Not a validated measurement tool and not part of the study data. Remove by deleting this file.
    ///
    /// F10 also starts a one-shot "eye texture content" check (see CaptureEyeTexture), but only while the XR display subsystem is running.
    /// It reads back a copy of the XR eye texture the app rendered and reports whether it holds scene content or only the camera's
    /// background colour. It never writes to the texture, never blocks the frame (AsyncGPUReadback), and runs only on the key press
    /// (2 s cooldown, one capture in flight). Nothing in this file runs per frame except one key test and the pose sampling.
    /// </summary>
    public class XRDiagnosticsProbe : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float ReportEverySeconds = 3f;
        const float MovedMetres = 0.02f;
        const float MovedDegrees = 2f;

        float nextReport;
        string lastReport = "";
        Vector3 lastHeadPos, lastLeftPos, lastRightPos;
        Quaternion lastHeadRot;
        bool movedHeadEver, movedLeftEver, movedRightEver, pressedLeftEver, pressedRightEver;
        bool haveBaseline;
        readonly List<InputDevice> devices = new List<InputDevice>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindFirstObjectByType<XRDiagnosticsProbe>() != null) return;
            var go = new GameObject("XRDiagnosticsProbe (runtime, not saved)");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<XRDiagnosticsProbe>();
        }

        void Update()
        {
            bool force = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            force = Input.GetKeyDown(KeyCode.F10);
            if (force) RequestEyeCapture();
#endif
            Sample();   // track "has it ever moved / been pressed" every frame
            if (!force && Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + ReportEverySeconds;
            string r = Report();
            if (force || r != lastReport)
            {
                lastReport = r;
                Debug.Log("[XRProbe] " + (force ? "(F10) " : "") + "\n" + r);
            }
        }

        // ---- one-shot XR eye texture content check (started by F10) ----

        const float CaptureCooldownSeconds = 2f;
        const int SampleStride = 8;            // look at every 8th pixel in x and y: cheap, and enough to tell "scene" from "flat colour"
        const int BackgroundTolerance = 12;    // byte difference from the camera background that still counts as "background"
        const int BrightThreshold = 160;       // a pixel with any channel above this is "bright" (the lab has emissive cyan/magenta/white)
        bool captureInFlight;
        float nextCaptureAllowed, captureStartedAt;
        int pendingSlices;
        readonly List<XRDisplaySubsystem> displayList = new List<XRDisplaySubsystem>();   // reused: no allocation per key press
        readonly int[] colourBuckets = new int[16 * 16 * 16];                              // reused for the colour histogram

        static bool XrDisplayRunning(List<XRDisplaySubsystem> scratch, out XRDisplaySubsystem display)
        {
            scratch.Clear();
            SubsystemManager.GetSubsystems(scratch);
            display = scratch.Count > 0 && scratch[0].running ? scratch[0] : null;
            return display != null;
        }

        void RequestEyeCapture()
        {
            XRDisplaySubsystem running;
            if (!XrDisplayRunning(displayList, out running))
            {
                Debug.Log("[XRProbe] eye check skipped: the XR display subsystem is not running (desktop path), so there is no eye texture to inspect.");
                return;
            }
            // A readback that never calls back (e.g. device loss) must not block the key forever: the in-flight flag expires after 10 s.
            if (captureInFlight && Time.unscaledTime - captureStartedAt < 10f) { Debug.Log("[XRProbe] eye capture ignored: previous capture still in flight."); return; }
            if (Time.unscaledTime < nextCaptureAllowed) { Debug.Log("[XRProbe] eye capture ignored: cooldown (" + CaptureCooldownSeconds + " s)."); return; }
            nextCaptureAllowed = Time.unscaledTime + CaptureCooldownSeconds;
            captureStartedAt = Time.unscaledTime;
            captureInFlight = true;
            StartCoroutine(CaptureEyeTexture());
        }

        /// <summary>
        /// Runs after the frame has rendered. Asks the GPU for an asynchronous COPY of each eye slice, so nothing is stalled and the
        /// eye texture is never modified. LIMITS: the texture belongs to the XR plugin's swapchain, so contents are best-effort;
        /// a texture the graphics API refuses to read back is reported as such, never guessed. This inspects what Unity RENDERED. It cannot
        /// tell what the simulator window then DISPLAYS: compare with the Unity Game view and the simulator window by eye.
        /// </summary>
        IEnumerator CaptureEyeTexture()
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var sb = new StringBuilder();
                Camera cam = Camera.main;
                sb.Append("EYE CAPTURE (F10)\n");
                sb.Append("  graphics API=").Append(SystemInfo.graphicsDeviceType).Append("  supportsAsyncGPUReadback=").Append(SystemInfo.supportsAsyncGPUReadback)
                  .Append("  colorSpace=").Append(QualitySettings.activeColorSpace).Append("  stereoRenderingMode=").Append(XRSettings.stereoRenderingMode)
                  .Append("  gameViewRenderMode=").Append(XRSettings.gameViewRenderMode).Append("  appFocused=").Append(Application.isFocused).Append('\n');
                if (cam == null) { sb.Append("  no Main Camera."); Finish(sb); yield break; }
                sb.Append("  Camera.stereoEnabled=").Append(cam.stereoEnabled).Append("  stereoTargetEye=").Append(cam.stereoTargetEye)
                  .Append("  clearFlags=").Append(cam.clearFlags).Append("  background=").Append(cam.backgroundColor.ToString("F2"))
                  .Append("  HDR=").Append(cam.allowHDR).Append("  MSAA=").Append(cam.allowMSAA).Append('\n');

                XRDisplaySubsystem display;
                XrDisplayRunning(displayList, out display);
                sb.Append("  XRDisplaySubsystem running=").Append(display != null).Append('\n');
                if (display == null) { sb.Append("  XR display is not running: nothing to inspect (this is the desktop path)."); Finish(sb); yield break; }

                int passes = display.GetRenderPassCount();
                sb.Append("  XR render passes=").Append(passes);
                if (passes < 1) { sb.Append("  -> none: Unity is not rendering an XR frame right now."); Finish(sb); yield break; }
                XRDisplaySubsystem.XRRenderPass pass;
                display.GetRenderPass(0, out pass);
                sb.Append("  render parameters in pass 0=").Append(pass.GetRenderParameterCount()).Append('\n');

                // GetRenderTexture(uint) takes an XR render-texture ID from the native plugin, NOT a pass index (passing 0 always returned null).
                // The accessor that takes a pass index is GetRenderTextureForRenderPass.
                RenderTexture rt = display.GetRenderTextureForRenderPass(0);
                if (rt == null) { sb.Append("  GetRenderTextureForRenderPass(0) returned null: Unity exposes no managed RenderTexture for this pass, so the eye image cannot be read back. Compare the Game view with the simulator window by eye instead."); Finish(sb); yield break; }
                sb.Append("  eye texture '").Append(rt.name).Append("' ").Append(rt.width).Append('x').Append(rt.height).Append(" dimension=").Append(rt.dimension)
                  .Append(" slices=").Append(rt.volumeDepth).Append(" format=").Append(rt.graphicsFormat).Append(" msaa=").Append(rt.antiAliasing).Append('\n');
                if (!SystemInfo.supportsAsyncGPUReadback) { sb.Append("  this graphics API cannot do asynchronous readback: use the Game view comparison instead."); Finish(sb); yield break; }

                // Sample bytes are what the texture stores. The camera background colour is a stored (display) value, so compare directly.
                Color32 bg = cam.backgroundColor;
                bool array = rt.dimension == TextureDimension.Tex2DArray;
                int slices = array ? Mathf.Clamp(rt.volumeDepth, 1, 2) : 1;
                Log(sb);   // header now; each slice reports itself when its copy arrives
                pendingSlices = slices;
                for (int z = 0; z < slices; z++)
                {
                    int slice = z;
                    AsyncGPUReadback.Request(rt, 0, 0, rt.width, 0, rt.height, array ? slice : 0, 1, TextureFormat.RGBA32,
                        (System.Action<AsyncGPUReadbackRequest>)(req => OnEyeSlice(req, slice, rt.width, rt.height, bg)));
                }
            }
            catch (System.Exception ex)
            {
                Debug.Log("[XRProbe] eye capture failed safely: " + ex.GetType().Name + ": " + ex.Message);
                captureInFlight = false;
            }
        }

        void OnEyeSlice(AsyncGPUReadbackRequest req, int slice, int w, int h, Color32 bg)
        {
            try
            {
                string eye = "slice " + slice + (slice == 0 ? " (left eye)" : " (right eye)");
                if (req.hasError) { Debug.Log("[XRProbe] " + eye + ": readback reported an error (this texture/API combination cannot be read). Use the Game view comparison."); return; }
                var data = req.GetData<byte>();
                if (data.Length < w * h * 4) { Debug.Log("[XRProbe] " + eye + ": readback returned " + data.Length + " bytes, expected " + (w * h * 4) + "."); return; }

                long n = 0, sumR = 0, sumG = 0, sumB = 0; int black = 0, background = 0, bright = 0;
                int[] buckets = colourBuckets;
                System.Array.Clear(buckets, 0, buckets.Length);
                for (int y = 0; y < h; y += SampleStride)
                    for (int x = 0; x < w; x += SampleStride)
                    {
                        int i = (y * w + x) * 4;
                        int r = data[i], g = data[i + 1], b = data[i + 2];
                        n++; sumR += r; sumG += g; sumB += b;
                        if (r <= 2 && g <= 2 && b <= 2) black++;
                        if (Mathf.Abs(r - bg.r) <= BackgroundTolerance && Mathf.Abs(g - bg.g) <= BackgroundTolerance && Mathf.Abs(b - bg.b) <= BackgroundTolerance) background++;
                        if (r > BrightThreshold || g > BrightThreshold || b > BrightThreshold) bright++;
                        buckets[((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4)]++;
                    }
                int distinct = 0;
                for (int k = 0; k < buckets.Length; k++) if (buckets[k] * 500L >= n) distinct++;   // buckets holding >= 0.2% of samples
                long differing = n - background;   // sampled pixels NOT within the tolerance of the expected background colour
                double fBlack = (double)black / n, fBg = (double)background / n, fBright = (double)bright / n;
                double fOther = 1.0 - fBlack - fBg;

                string verdict;
                if (fBlack >= 0.98) verdict = "ALL BLACK: the eye texture is empty/unwritten (or the readback returned zeros).";
                else if (fBright >= 0.002 || fOther >= 0.02) verdict = "CONTAINS SCENE CONTENT: Unity rendered something other than the flat background into this eye.";
                else if (fBg >= 0.98) verdict = "ONLY BACKGROUND: nothing but the camera background colour was rendered into this eye.";
                else verdict = "INCONCLUSIVE: mostly dark/near-background with very little else. Compare with the Game view.";
                Debug.Log("[XRProbe] " + eye + ": " + verdict + "\n"
                    + "    sampled pixels=" + n + " (every " + SampleStride + "th pixel in x and y)  mean RGB (0-255)=(" + (sumR / n) + "," + (sumG / n) + "," + (sumB / n)
                    + ")  expected background=(" + bg.r + "," + bg.g + "," + bg.b + ")/255 = (" + (bg.r / 255f).ToString("F2") + "," + (bg.g / 255f).ToString("F2") + "," + (bg.b / 255f).ToString("F2") + ")\n"
                    + "    pixels DIFFERING from the background (tolerance +/-" + BackgroundTolerance + "): " + differing + " of " + n + " (" + ((double)differing / n * 100).ToString("F1") + "%)"
                    + "  |  matching background=" + (fBg * 100).ToString("F1")
                    + "%  black=" + (fBlack * 100).ToString("F1") + "%  bright=" + (fBright * 100).ToString("F2") + "%  other=" + (fOther * 100).ToString("F1")
                    + "%  distinct colours(>=0.2%)=" + distinct);
            }
            catch (System.Exception ex)
            {
                Debug.Log("[XRProbe] slice " + slice + ": analysis failed safely: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (--pendingSlices <= 0) captureInFlight = false;
            }
        }

        void Finish(StringBuilder sb)
        {
            Log(sb);
            captureInFlight = false;
        }

        static void Log(StringBuilder sb) { Debug.Log("[XRProbe] " + sb.ToString()); }

        void Sample()
        {
            Vector3 h, l, rr; Quaternion hr;
            bool hh = TryPose(XRNode.Head, out h, out hr);
            bool ll = TryPos(XRNode.LeftHand, out l);
            bool rrOk = TryPos(XRNode.RightHand, out rr);
            if (!haveBaseline)
            {
                if (hh) { lastHeadPos = h; lastHeadRot = hr; }
                if (ll) lastLeftPos = l;
                if (rrOk) lastRightPos = rr;
                haveBaseline = hh || ll || rrOk;
                return;
            }
            if (hh && ((h - lastHeadPos).magnitude > MovedMetres || Quaternion.Angle(hr, lastHeadRot) > MovedDegrees)) movedHeadEver = true;
            if (ll && (l - lastLeftPos).magnitude > MovedMetres) movedLeftEver = true;
            if (rrOk && (rr - lastRightPos).magnitude > MovedMetres) movedRightEver = true;
            if (AnyButton(XRNode.LeftHand)) pressedLeftEver = true;
            if (AnyButton(XRNode.RightHand)) pressedRightEver = true;
        }

        static bool TryPos(XRNode node, out Vector3 p)
        {
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            p = Vector3.zero;
            return d.isValid && d.TryGetFeatureValue(CommonUsages.devicePosition, out p);
        }

        static bool TryPose(XRNode node, out Vector3 p, out Quaternion q)
        {
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            p = Vector3.zero; q = Quaternion.identity;
            return d.isValid && d.TryGetFeatureValue(CommonUsages.devicePosition, out p) && d.TryGetFeatureValue(CommonUsages.deviceRotation, out q);
        }

        static bool AnyButton(XRNode node)
        {
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return false;
            bool b; float f;
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out b) && b) return true;
            if (d.TryGetFeatureValue(CommonUsages.gripButton, out b) && b) return true;
            if (d.TryGetFeatureValue(CommonUsages.primaryButton, out b) && b) return true;
            if (d.TryGetFeatureValue(CommonUsages.secondaryButton, out b) && b) return true;
            if (d.TryGetFeatureValue(CommonUsages.trigger, out f) && f > 0.5f) return true;
            return false;
        }

        string Report()
        {
            var sb = new StringBuilder();
            // 1. loader / session / display
            sb.Append("1 LOADER  : XRSettings.isDeviceActive=").Append(XRSettings.isDeviceActive)
              .Append("  loadedDeviceName='").Append(XRSettings.loadedDeviceName).Append("'\n");
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            sb.Append("2 DISPLAY : XRDisplaySubsystem count=").Append(displays.Count);
            foreach (var d in displays) sb.Append("  running=").Append(d.running);
            sb.Append("  eyeTexture=").Append(XRSettings.eyeTextureWidth).Append("x").Append(XRSettings.eyeTextureHeight).Append('\n');

            // 2. devices
            devices.Clear();
            InputDevices.GetDevices(devices);
            sb.Append("3 DEVICES : ").Append(devices.Count).Append(" XR device(s): ");
            for (int i = 0; i < devices.Count; i++) sb.Append(i > 0 ? "; " : "").Append(devices[i].name).Append(" [").Append(devices[i].characteristics).Append(']');
            sb.Append('\n');

            // 3. head
            Vector3 hp; Quaternion hr;
            bool head = TryPose(XRNode.Head, out hp, out hr);
            Camera cam = Camera.main;
            sb.Append("4 HEAD    : device pose ").Append(head ? "readable " + hp.ToString("F2") : "NOT readable")
              .Append("  | MainCamera world pos ").Append(cam != null ? cam.transform.position.ToString("F2") : "none")
              .Append("  | head moved since start: ").Append(movedHeadEver ? "YES" : "no").Append('\n');

            // 4. controllers
            sb.Append("5 LEFT    : ").Append(ControllerLine(XRNode.LeftHand, movedLeftEver, pressedLeftEver)).Append('\n');
            sb.Append("6 RIGHT   : ").Append(ControllerLine(XRNode.RightHand, movedRightEver, pressedRightEver)).Append('\n');

            // 5. scene objects driven by tracking
            sb.Append("7 RIG     : ").Append(RigLine("Left Controller")).Append(" | ").Append(RigLine("Right Controller")).Append('\n');

            // 6. ray interactors and gaze camera
            sb.Append("8 RAYS    : ").Append(RayLine("Left Ray Interactor")).Append(" | ").Append(RayLine("Right Ray Interactor")).Append('\n');
            var gaze = FindFirstObjectByType<GazeFocusSystem>();
            if (gaze != null)
            {
                Camera gc = gaze.gazeCamera != null ? gaze.gazeCamera : Camera.main;
                sb.Append("9 GAZE    : camera='").Append(gc != null ? gc.name : "none").Append("' forward=")
                  .Append(gc != null ? gc.transform.forward.ToString("F2") : "-")
                  .Append("  focus=").Append(gaze.FocusedTarget != null ? gaze.FocusedTarget.DisplayName : "nothing")
                  .Append("  selected=").Append(gaze.SelectedTarget != null ? gaze.SelectedTarget.DisplayName : "none").Append('\n');
            }
            var panel = XrHudPanel.Instance;   // not FindFirstObjectByType: the panel is HideFlags.DontSave, which the search skips
            sb.Append("11 XR HUD  : ").Append(panel == null ? "XrHudPanel NOT created" : panel.DebugState()).Append('\n');
            var look = FindFirstObjectByType<DesktopLookController>();
            sb.Append("10 DESKTOP : DesktopLookController ").Append(look == null ? "absent" : (XRSettings.isDeviceActive && !look.forceDesktopLook ? "STANDING DOWN (XR active: mouse/arrows will not rotate the view)" : "active"));
            return sb.ToString();
        }

        static string ControllerLine(XRNode node, bool movedEver, bool pressedEver)
        {
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return "no device (not tracked/connected, or its interaction profile is not enabled)";
            Vector3 p; Quaternion q; bool trig, grip, prim, sec; float tv, gv;
            string pos = d.TryGetFeatureValue(CommonUsages.devicePosition, out p) ? p.ToString("F2") : "n/a";
            string rot = d.TryGetFeatureValue(CommonUsages.deviceRotation, out q) ? q.eulerAngles.ToString("F0") : "n/a";
            d.TryGetFeatureValue(CommonUsages.triggerButton, out trig);
            d.TryGetFeatureValue(CommonUsages.gripButton, out grip);
            d.TryGetFeatureValue(CommonUsages.primaryButton, out prim);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out sec);
            d.TryGetFeatureValue(CommonUsages.trigger, out tv);
            d.TryGetFeatureValue(CommonUsages.grip, out gv);
            return "'" + d.name + "' pos " + pos + " rot " + rot + " | trigger " + tv.ToString("F2") + (trig ? "(down)" : "") + " grip " + gv.ToString("F2") + (grip ? "(down)" : "")
                 + " A/X " + (prim ? "down" : "up") + " B/Y " + (sec ? "down" : "up")
                 + " | moved since start: " + (movedEver ? "YES" : "no") + ", any button seen: " + (pressedEver ? "YES" : "no");
        }

        static string RigLine(string objName)
        {
            GameObject go = GameObject.Find(objName);
            return go == null ? objName + ": MISSING" : objName + " world pos " + go.transform.position.ToString("F2");
        }

        static string RayLine(string objName)
        {
            GameObject go = GameObject.Find(objName);
            if (go == null) return objName + ": MISSING";
            var ray = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            if (ray == null) return objName + ": no XRRayInteractor";
            RaycastHit hit;
            bool got = ray.TryGetCurrent3DRaycastHit(out hit);
            return objName + (ray.isActiveAndEnabled ? "" : " (DISABLED)") + ": hit " + (got ? "'" + hit.collider.name + "' at " + hit.distance.ToString("F2") + " m" : "nothing")
                 + ", hovering " + ray.interactablesHovered.Count + ", selecting " + ray.interactablesSelected.Count;
        }
#endif
    }
}
