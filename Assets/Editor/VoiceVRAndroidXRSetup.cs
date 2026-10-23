#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Android / Meta Quest configuration for the EXISTING VoiceVR project, applied through Unity's own XR Management, OpenXR and
    /// PlayerSettings APIs (not by editing YAML while the Editor holds those assets in memory).
    ///
    ///  * Report (read-only): prints the current state of the Windows Standalone and Android XR configuration.
    ///  * Apply: configures ANDROID ONLY: OpenXR loader, Meta Quest Support + Quest/Touch interaction profiles, IL2CPP, ARM64, minimum API 29.
    ///    It never changes the Windows Standalone configuration, never switches the active build target, and aborts and restores
    ///    the Standalone OpenXR render mode (Multi Pass = 0) if anything changed it. A timestamped backup of the touched settings is
    ///    written to the git-ignored .backups folder first (never overwriting an earlier backup).
    ///
    /// It does NOT install packages, replace the rig or scene, or change the voice pipeline. Nothing here is validated on a headset.
    /// Menu: Group05 > VoiceVR > Android XR.  Batch (Editor closed): -executeMethod Group05.VoiceVR.EditorTools.VoiceVRAndroidXRSetup.ReportBatch / ApplyBatch
    /// </summary>
    public static class VoiceVRAndroidXRSetup
    {
        const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string LoaderSettingsKey = "com.unity.xr.management.loader_settings";

        // feature ids read from Assets/XR/Settings/OpenXR Package Settings.asset (Android block)
        static readonly string[] QuestFeatureIds =
        {
            "com.unity.openxr.feature.metaquest",               // Meta Quest Support
            "com.unity.openxr.feature.input.oculustouch",       // Oculus Touch Controller Profile
            "com.unity.openxr.feature.input.metaquestplus",     // Meta Quest Touch Plus Controller Profile
            "com.unity.openxr.feature.input.metaquestpro"       // Meta Quest Touch Pro Controller Profile
        };

        const AndroidSdkVersions MinimumApi = AndroidSdkVersions.AndroidApiLevel29;   // Unity's Meta Quest build profile default

        // ---------- menu / batch entry points ----------

        [MenuItem("Group05/VoiceVR/Android XR/Report Quest (Android) setup status (read-only)")]
        public static void Report()
        {
            string text = BuildReport();
            Debug.Log("[VoiceVRAndroidXRSetup] STATUS (read-only)\n" + text);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("VoiceVR Android XR status", text + "\n(Also in the Console.)", "OK");
        }

        [MenuItem("Group05/VoiceVR/Android XR/Apply Quest (Android) XR setup")]
        public static void Apply()
        {
            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Apply Android XR setup",
                    "This changes ANDROID settings only (OpenXR loader, Meta Quest features, IL2CPP, ARM64, minimum API 29).\n" +
                    "Windows Standalone XR is verified unchanged afterwards. A backup is written to .backups first.\n\nContinue?", "Apply", "Cancel"))
                return;
            string result = ApplyInternal();
            Debug.Log("[VoiceVRAndroidXRSetup] " + result);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("VoiceVR Android XR setup", result, "OK");
        }

        public static void ReportBatch() { Report(); }

        [MenuItem("Group05/VoiceVR/Android XR/Apply Gamma color space + Android Vulkan only (Option B)")]
        public static void ApplyOptionB()
        {
            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Apply Option B",
                    "Sets the project color space back to GAMMA and Android graphics APIs to VULKAN ONLY (automatic off).\n" +
                    "Nothing else is changed; the Android XR setup and everything on Windows are verified unchanged afterwards.\n" +
                    "Unity will reimport textures after the color space change, which can take a few minutes.\n\nContinue?", "Apply", "Cancel"))
                return;
            string result = ApplyOptionBInternal();
            Debug.Log("[VoiceVRAndroidXRSetup] OPTION B: " + result);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("VoiceVR Option B", result, "OK");
        }

        public static void ApplyOptionBBatch()
        {
            string result = ApplyOptionBInternal();
            Debug.Log("[VoiceVRAndroidXRSetup] OPTION B: " + result);
            EditorApplication.Exit(result.StartsWith("ABORTED") || result.StartsWith("ATTENTION") ? 1 : 0);
        }

        public static void ApplyBatch()
        {
            string result = ApplyInternal();
            Debug.Log("[VoiceVRAndroidXRSetup] " + result);
            EditorApplication.Exit(result.StartsWith("ABORTED") ? 1 : 0);
        }

        // ---------- report ----------

        static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Unity " + Application.unityVersion + "; active build target: " + EditorUserBuildSettings.activeBuildTarget);
            sb.AppendLine("Project color space: " + PlayerSettings.colorSpace + "   (the Windows/Simulator demo was built on Gamma)");

            sb.AppendLine("-- Windows Standalone (must stay as it is)");
            AppendTarget(sb, BuildTargetGroup.Standalone);

            sb.AppendLine("-- Android / Meta Quest");
            sb.AppendLine("Android build support installed: " + BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android));
            AppendTarget(sb, BuildTargetGroup.Android);
            var nbt = NamedBuildTarget.Android;
            sb.AppendLine("Scripting backend: " + PlayerSettings.GetScriptingBackend(nbt) + "   (Quest: IL2CPP)");
            sb.AppendLine("Target architectures: " + PlayerSettings.Android.targetArchitectures + "   (Quest: ARM64 only)");
            sb.AppendLine("Minimum API level: " + PlayerSettings.Android.minSdkVersion + "   (Quest: Android 10 / API 29 or higher)");
            sb.AppendLine("Target API level: " + PlayerSettings.Android.targetSdkVersion + "   (Meta: Automatic / highest installed)");
            sb.AppendLine("Android graphics APIs: " + DescribeGraphicsApis(BuildTarget.Android) + "   (Gamma needs no OpenGLES3: Vulkan only)");

            sb.AppendLine("-- Build scene list");
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) sb.AppendLine("  " + (s.enabled ? "[x] " : "[ ] ") + s.path);
            return sb.ToString();
        }

        static void AppendTarget(StringBuilder sb, BuildTargetGroup group)
        {
            bool hasManager = false;
            EditorBuildSettings.TryGetConfigObject(LoaderSettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget != null) hasManager = perTarget.HasManagerSettingsForBuildTarget(group);
            sb.AppendLine(group + ": XR Plug-in Management settings exist: " + hasManager);
            sb.AppendLine(group + ": OpenXR loader assigned: " + XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, group));

            OpenXRSettings oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            sb.AppendLine(group + ": OpenXR render mode: " + (oxr != null ? oxr.renderMode.ToString() + " (" + (int)oxr.renderMode + ")" : "no OpenXR settings"));
            foreach (string id in QuestFeatureIds)
            {
                OpenXRFeature f = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
                sb.AppendLine(group + ":   " + id + " = " + (f == null ? "not available" : f.enabled ? "ENABLED" : "disabled"));
            }
        }

        // ---------- apply ----------

        static string ApplyInternal()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return "ABORTED: leave Play Mode first. Nothing was changed.";
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                return "ABORTED: Android build support is not installed in this Unity Editor. Nothing was changed.";

            // Snapshot of everything on the Windows side that must survive.
            OpenXRSettings standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (standalone == null) return "ABORTED: no Windows Standalone OpenXR settings found. Nothing was changed.";
            OpenXRSettings.RenderMode standaloneMode = standalone.renderMode;
            bool standaloneLoader = XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Standalone);
            var activeBefore = EditorUserBuildSettings.activeBuildTarget;

            string backupDir;
            try { backupDir = Backup(); }
            catch (Exception ex) { return "ABORTED: could not write the backup (" + ex.Message + "). Nothing was changed."; }

            var changes = new List<string>();
            try
            {
                // 1. XR Plug-in Management for Android: create the settings objects if missing, then assign the OpenXR loader.
                EditorBuildSettings.TryGetConfigObject(LoaderSettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
                if (perTarget == null) return "ABORTED: XR Plug-in Management settings were not found. Backup: " + backupDir;
                if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                {
                    perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                    changes.Add("created XR general settings for Android");
                }
                if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                {
                    perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                    changes.Add("created XR manager settings for Android");
                }
                XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Android))
                {
                    if (XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderType, BuildTargetGroup.Android)) changes.Add("assigned the OpenXR loader to Android");
                    else changes.Add("WARNING: could not assign the OpenXR loader to Android");
                }

                // 2. OpenXR features for Android: Meta Quest Support and the Quest controller profiles.
                foreach (string id in QuestFeatureIds)
                {
                    OpenXRFeature f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
                    if (f == null) { changes.Add("WARNING: OpenXR feature not available: " + id); continue; }
                    if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); changes.Add("enabled OpenXR feature " + id + " for Android"); }
                }

                // 3. Android player settings Meta documents for Quest (Unity's Meta Quest build profile defaults).
                NamedBuildTarget nbt = NamedBuildTarget.Android;
                if (PlayerSettings.GetScriptingBackend(nbt) != ScriptingImplementation.IL2CPP)
                {
                    PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
                    changes.Add("Android scripting backend -> IL2CPP");
                }
                if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                {
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    changes.Add("Android target architecture -> ARM64");
                }
                if (PlayerSettings.Android.minSdkVersion < MinimumApi)
                {
                    PlayerSettings.Android.minSdkVersion = MinimumApi;
                    changes.Add("Android minimum API level -> " + MinimumApi);
                }
                if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevelAuto)
                {
                    PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                    changes.Add("Android target API level -> Automatic (highest installed)");
                }

                EditorUtility.SetDirty(perTarget);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                return "ABORTED with an error after the backup (" + backupDir + "): " + ex.GetType().Name + ": " + ex.Message
                       + (changes.Count > 0 ? "\nChanges made before the error: " + string.Join("; ", changes.ToArray()) : "");
            }

            // 4. Verify Windows Standalone is exactly as before; restore the render mode if anything touched it.
            var problems = new List<string>();
            OpenXRSettings standaloneAfter = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (standaloneAfter == null || standaloneAfter.renderMode != standaloneMode)
            {
                if (standaloneAfter != null) { standaloneAfter.renderMode = standaloneMode; EditorUtility.SetDirty(standaloneAfter); AssetDatabase.SaveAssets(); }
                problems.Add("Standalone OpenXR render mode had changed and was restored to " + standaloneMode);
            }
            if (XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Standalone) != standaloneLoader)
                problems.Add("the Standalone OpenXR loader assignment changed (check XR Plug-in Management > Windows)");
            if (EditorUserBuildSettings.activeBuildTarget != activeBefore)
                problems.Add("the active build target changed from " + activeBefore + " to " + EditorUserBuildSettings.activeBuildTarget);

            string summary = (changes.Count == 0 ? "Android was already configured; nothing to change." : "Android changes: " + string.Join("; ", changes.ToArray()) + ".")
                           + "\nWindows Standalone OpenXR render mode: " + standaloneMode + " (" + (int)standaloneMode + "), loader assigned: " + standaloneLoader + "."
                           + "\nBackup: " + backupDir
                           + "\nNot validated on a headset. Run 'Report' to review the result.";
            return problems.Count == 0 ? summary : "ATTENTION: " + string.Join("; ", problems.ToArray()) + "\n" + summary;
        }

        // ---------- Option B: Gamma color space + Android Vulkan only ----------

        static string DescribeGraphicsApis(BuildTarget target)
        {
            bool auto = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(target);
            string[] names = new string[apis.Length];
            for (int i = 0; i < apis.Length; i++) names[i] = apis[i].ToString();
            return (auto ? "automatic" : "manual") + " [" + string.Join(", ", names) + "]";
        }

        // Everything Option B must NOT change: the Windows XR setup, the Android XR setup, the Android player settings, the active build target.
        sealed class Snapshot
        {
            public OpenXRSettings.RenderMode StandaloneRenderMode;
            public bool StandaloneLoader, AndroidLoader, StandaloneTouch;
            public bool[] AndroidFeatures = new bool[QuestFeatureIds.Length];
            public ScriptingImplementation AndroidBackend;
            public AndroidArchitecture AndroidArch;
            public AndroidSdkVersions AndroidMinSdk, AndroidTargetSdk;
            public BuildTarget ActiveTarget;
            public string StandaloneGraphics;
        }

        static Snapshot TakeSnapshot()
        {
            var s = new Snapshot();
            OpenXRSettings oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            s.StandaloneRenderMode = oxr != null ? oxr.renderMode : (OpenXRSettings.RenderMode)(-1);
            s.StandaloneLoader = XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Standalone);
            s.AndroidLoader = XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Android);
            OpenXRFeature touch = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Standalone, "com.unity.openxr.feature.input.oculustouch");
            s.StandaloneTouch = touch != null && touch.enabled;
            for (int i = 0; i < QuestFeatureIds.Length; i++)
            {
                OpenXRFeature f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, QuestFeatureIds[i]);
                s.AndroidFeatures[i] = f != null && f.enabled;
            }
            s.AndroidBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            s.AndroidArch = PlayerSettings.Android.targetArchitectures;
            s.AndroidMinSdk = PlayerSettings.Android.minSdkVersion;
            s.AndroidTargetSdk = PlayerSettings.Android.targetSdkVersion;
            s.ActiveTarget = EditorUserBuildSettings.activeBuildTarget;
            s.StandaloneGraphics = DescribeGraphicsApis(BuildTarget.StandaloneWindows64);
            return s;
        }

        static void AddDiff(List<string> diffs, string what, object before, object after)
        {
            if (!Equals(before, after)) diffs.Add(what + ": " + before + " -> " + after);
        }

        static List<string> CompareSnapshots(Snapshot a, Snapshot b)
        {
            var d = new List<string>();
            AddDiff(d, "Windows OpenXR render mode", a.StandaloneRenderMode, b.StandaloneRenderMode);
            AddDiff(d, "Windows OpenXR loader assigned", a.StandaloneLoader, b.StandaloneLoader);
            AddDiff(d, "Windows Oculus Touch profile enabled", a.StandaloneTouch, b.StandaloneTouch);
            AddDiff(d, "Windows graphics APIs", a.StandaloneGraphics, b.StandaloneGraphics);
            AddDiff(d, "Android OpenXR loader assigned", a.AndroidLoader, b.AndroidLoader);
            for (int i = 0; i < QuestFeatureIds.Length; i++) AddDiff(d, "Android feature " + QuestFeatureIds[i], a.AndroidFeatures[i], b.AndroidFeatures[i]);
            AddDiff(d, "Android scripting backend", a.AndroidBackend, b.AndroidBackend);
            AddDiff(d, "Android architectures", a.AndroidArch, b.AndroidArch);
            AddDiff(d, "Android minimum API", a.AndroidMinSdk, b.AndroidMinSdk);
            AddDiff(d, "Android target API", a.AndroidTargetSdk, b.AndroidTargetSdk);
            AddDiff(d, "active build target", a.ActiveTarget, b.ActiveTarget);
            return d;
        }

        static string ApplyOptionBInternal()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return "ABORTED: leave Play Mode first. Nothing was changed.";
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                return "ABORTED: Android build support is not installed in this Unity Editor. Nothing was changed.";
            if (!SystemInfoSupportsVulkanOnAndroid())
                return "ABORTED: Vulkan is not available as an Android graphics API in this Editor. Nothing was changed.";

            Snapshot before = TakeSnapshot();
            ColorSpace colorBefore = PlayerSettings.colorSpace;
            string apisBefore = DescribeGraphicsApis(BuildTarget.Android);

            string backupDir;
            try { backupDir = Backup("xr_optionb_"); }
            catch (Exception ex) { return "ABORTED: could not write the backup (" + ex.Message + "). Nothing was changed."; }

            var changes = new List<string>();
            try
            {
                if (colorBefore != ColorSpace.Gamma)
                {
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                    changes.Add("color space " + colorBefore + " -> Gamma");
                }

                GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                bool vulkanOnly = !PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) && apis.Length == 1 && apis[0] == GraphicsDeviceType.Vulkan;
                if (!vulkanOnly)
                {
                    PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                    PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
                    changes.Add("Android graphics APIs " + apisBefore + " -> manual [Vulkan]");
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                return "ABORTED with an error after the backup (" + backupDir + "): " + ex.GetType().Name + ": " + ex.Message
                       + (changes.Count > 0 ? "\nChanges made before the error: " + string.Join("; ", changes.ToArray()) : "");
            }

            var problems = CompareSnapshots(before, TakeSnapshot());
            string apisAfter = DescribeGraphicsApis(BuildTarget.Android);
            if (PlayerSettings.colorSpace != ColorSpace.Gamma) problems.Add("color space is " + PlayerSettings.colorSpace + ", expected Gamma");
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) || !apisAfter.Equals("manual [Vulkan]")) problems.Add("Android graphics APIs are " + apisAfter + ", expected manual [Vulkan]");

            string summary = (changes.Count == 0 ? "Already applied; nothing to change." : "Changes: " + string.Join("; ", changes.ToArray()) + ".")
                           + "\nColor space now: " + PlayerSettings.colorSpace + "; Android graphics APIs now: " + apisAfter + "."
                           + "\nWindows render mode: " + before.StandaloneRenderMode + " (" + (int)before.StandaloneRenderMode + "), Android XR setup compared before/after."
                           + "\nBackup: " + backupDir + "\nNot validated visually or on a headset. Run 'Report' to review.";
            return problems.Count == 0 ? summary : "ATTENTION: " + string.Join("; ", problems.ToArray()) + "\n" + summary;
        }

        static bool SystemInfoSupportsVulkanOnAndroid()
        {
            // PlayerSettings accepts any GraphicsDeviceType; Vulkan is a valid Android API when Android support is installed.
            return BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // ---------- backup (never overwrites; inside the project, outside Assets, git-ignored) ----------

        static string Backup(string prefix = "xr_android_setup_")
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string dir = Path.Combine(root, ".backups", prefix + stamp);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, ".backups", prefix + stamp + "_" + n);
            Directory.CreateDirectory(dir);

            string[] files =
            {
                "ProjectSettings/ProjectSettings.asset",
                "ProjectSettings/EditorBuildSettings.asset",
                "Assets/XR/XRGeneralSettingsPerBuildTarget.asset",
                "Assets/XR/Settings/OpenXR Package Settings.asset",
                "Assets/XR/Settings/OpenXR Editor Settings.asset",
                "Assets/XR/Loaders/OpenXRLoader.asset"
            };
            var manifest = new StringBuilder("relative_path,sha256,bytes\n");
            foreach (string rel in files)
            {
                string src = Path.Combine(root, rel);
                if (!File.Exists(src)) continue;
                string dst = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, false);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream fs = File.OpenRead(src))
                    manifest.Append(rel).Append(',').Append(BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "")).Append(',').Append(new FileInfo(src).Length).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "SHA256_manifest.csv"), manifest.ToString());
            return dir;
        }
    }
}
#endif
