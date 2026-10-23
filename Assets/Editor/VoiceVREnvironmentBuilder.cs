#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Builds the "Neon Sci-Fi Research Lab" inside the EXISTING scene Assets/Scenes/VoiceVR_Main.unity:
    ///   * root "Environment_NeonLab"      decorative scenery (floor deck, walls, ceiling, platform, pedestals, stations, lights). NO SpatialTarget.
    ///   * root "MovableAssets_NeonLab"    six movable assets, each with colliders and a SpatialTarget (the component the existing gaze/voice pipeline requires).
    /// It never touches the Main Camera, XR Origin, GazeFocusSystem, DesktopVoiceInput, HUD, telemetry, the three original targets,
    /// Build Settings, packages or XR settings. It only changes the scene's ambient lighting (RenderSettings).
    ///
    /// Idempotent: re-running removes ONLY the two roots named above (and nothing else) and rebuilds them; materials under
    /// Assets/Materials/Environment/ are updated in place, so no duplicates are created.
    /// All-or-nothing: the scene is saved only if every check in Verify() passes; otherwise the scene is reloaded from disk unchanged.
    /// Run from the menu: Group05 > VoiceVR > Build Neon Lab Environment.
    /// </summary>
    public static class VoiceVREnvironmentBuilder
    {
        const string ScenePath = VoiceVRSceneBuilder.ScenePath;
        public const string EnvRootName = "Environment_NeonLab";
        public const string AssetsRootName = "MovableAssets_NeonLab";
        const string MaterialFolder = "Assets/Materials/Environment";

        // Room: inner wall faces sit 0.4 m outside the gaze system's play area (arenaHalfExtent 7), so a target clamped to the edge
        // of the play area is never inside a wall.
        const float Inner = 7.4f;
        const float WallHeight = 4.4f;
        static readonly Vector3 PlatformCenter = new Vector3(0f, 0f, 3f);
        const float PedestalTop = 0.82f;

        // ---------------------------------------------------------------- entry points

        [MenuItem("Group05/VoiceVR/Build Neon Lab Environment")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report;
            bool ok = Build(out report);
            EditorUtility.DisplayDialog(ok ? "Neon Lab built" : "Neon Lab NOT built", report, "OK");
        }

        /// <summary>For: Unity -batchmode -nographics -projectPath ... -executeMethod Group05.VoiceVR.EditorTools.VoiceVREnvironmentBuilder.BuildBatch -quit
        /// Exit code 0 = built, verified and saved; 2 = refused or failed verification (scene left unchanged on disk).</summary>
        public static void BuildBatch()
        {
            string report;
            bool ok = Build(out report);
            Debug.Log("[VoiceVREnvironmentBuilder] " + report);
            EditorApplication.Exit(ok ? 0 : 2);
        }

        public static bool Build(out string report)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) { report = "Exit Play Mode first. Nothing was changed."; return false; }
            if (!File.Exists(ScenePath)) { report = ScenePath + " does not exist. Nothing was changed."; return false; }
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var problems = Preflight(scene);
            if (problems.Count > 0)
            {
                report = "Refused. Nothing was changed.\n- " + string.Join("\n- ", problems.ToArray());
                Debug.LogError("[VoiceVREnvironmentBuilder] " + report);
                return false;
            }

            RemoveOwnRoots(scene);
            Mats m = CreateMaterials();
            GameObject env = new GameObject(EnvRootName);
            GameObject assets = new GameObject(AssetsRootName);
            BuildEnvironment(env.transform, m);
            BuildMovableAssets(assets.transform, m);
            ApplyLabLighting();

            var gaze = Object.FindFirstObjectByType<GazeFocusSystem>();
            Camera cam = Camera.main;
            var issues = Verify(scene, env, assets, gaze, cam);
            if (issues.Count > 0)
            {
                // Discard the unsaved work: reopen the scene from disk exactly as it was.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                report = "Verification FAILED. The scene was NOT saved (reloaded from disk unchanged).\n- " + string.Join("\n- ", issues.ToArray());
                Debug.LogError("[VoiceVREnvironmentBuilder] " + report);
                return false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            report = "Built " + EnvRootName + " and " + AssetsRootName + " (6 movable assets) in " + ScenePath + ". All builder checks passed. "
                   + "These checks are geometry/physics checks in the Editor only: they do NOT prove how it looks or behaves in Play Mode.";
            return true;
        }

        // ---------------------------------------------------------------- preflight / cleanup

        static List<string> Preflight(Scene scene)
        {
            var p = new List<string>();
            if (Object.FindObjectsByType<GazeFocusSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1)
                p.Add("Expected exactly one GazeFocusSystem in the scene.");
            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cams.Length != 1 || !cams[0].CompareTag("MainCamera")) p.Add("Expected exactly one Camera, tagged MainCamera (this builder never adds a camera).");
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length < 1) p.Add("The XR Origin was not found; refusing to build on an unexpected scene.");
            if (Shader.Find("Standard") == null) p.Add("The built-in 'Standard' shader was not found.");
            var gaze = Object.FindFirstObjectByType<GazeFocusSystem>();
            if (gaze != null && (gaze.arenaHalfExtent < 6.4f || gaze.arenaHalfExtent > 7.2f))
                p.Add("GazeFocusSystem.arenaHalfExtent is " + gaze.arenaHalfExtent + "; this layout assumes about 7 (walls are at " + Inner + ").");
            return p;
        }

        static void RemoveOwnRoots(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == EnvRootName || go.name == AssetsRootName)
                    Object.DestroyImmediate(go);
        }

        // ---------------------------------------------------------------- materials (reusable assets)

        sealed class Mats
        {
            public Material FloorDeck, FloorSeam, WallBase, WallPanel, Trim, Platform, Ceiling;
            public Material Cyan, Magenta, Amber, Green, Red, WhitePanel, ScreenDim, Hazard;
            public Material CoreBody, CoreTrim, CoreHalo, CoreGlow, CoreGlass;
            public Material DataBody, DataTrim, DataHolo;
            public Material CanBody, CanDark, CanGlass, CanGlow;
            public Material CrysBase, CrysTrim, CrysMagenta, CrysViolet, CrysHalo, CrysGlass, CrysCore;
            public Material PowBody, PowTrim, Brass;
            public Material RobBody, RobJoint;
        }

        static Mats CreateMaterials()
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);
            var m = new Mats();
            m.FloorDeck = Std("Env_FloorDeck", new Color(0.045f, 0.055f, 0.075f), 0.9f, 0.75f);
            m.FloorSeam = Std("Env_FloorSeam", new Color(0.01f, 0.012f, 0.02f), 0.5f, 0.3f, new Color(0f, 0.55f, 0.65f), 0.5f);
            m.WallBase = Std("Env_WallBase", new Color(0.06f, 0.07f, 0.10f), 0.8f, 0.5f);
            m.WallPanel = Std("Env_WallPanel", new Color(0.11f, 0.13f, 0.18f), 0.85f, 0.6f);
            m.Trim = Std("Env_Trim", new Color(0.035f, 0.04f, 0.055f), 0.9f, 0.6f);
            m.Platform = Std("Env_Platform", new Color(0.08f, 0.09f, 0.125f), 0.9f, 0.65f);
            m.Ceiling = Std("Env_Ceiling", new Color(0.045f, 0.05f, 0.065f), 0.6f, 0.4f);
            m.Hazard = Std("Env_HazardDark", new Color(0.02f, 0.02f, 0.025f), 0.3f, 0.3f);
            m.Cyan = Glow("Env_CyanGlow", new Color(0f, 0.9f, 1f), 2.2f);
            m.Magenta = Glow("Env_MagentaGlow", new Color(1f, 0.1f, 0.8f), 2.0f);
            m.Amber = Glow("Env_AmberWarning", new Color(1f, 0.55f, 0.05f), 1.6f);   // restrained: used for small accents only
            m.Green = Glow("Env_GreenGlow", new Color(0.1f, 0.95f, 0.4f), 1.8f);
            m.Red = Glow("Env_RedGlow", new Color(1f, 0.15f, 0.1f), 2.0f);
            m.WhitePanel = Glow("Env_WhitePanelLight", new Color(0.75f, 0.9f, 1f), 1.8f);
            m.ScreenDim = Std("Env_ScreenDim", new Color(0.01f, 0.03f, 0.04f), 0.2f, 0.8f, new Color(0f, 0.30f, 0.40f), 1.0f);

            m.CoreBody = Std("Obj_EnergyCore_Frame", new Color(0.16f, 0.18f, 0.23f), 0.9f, 0.6f);
            m.CoreGlow = Glow("Obj_EnergyCore_Glow", new Color(0f, 0.85f, 1f), 3.0f);
            m.CoreHalo = Additive("Obj_EnergyCore_Halo", new Color(0f, 0.55f, 0.9f, 0.30f));
            m.CoreTrim = Std("Obj_EnergyCore_Trim", new Color(0.30f, 0.33f, 0.40f), 0.95f, 0.7f);
            m.CoreGlass = Glass("Obj_EnergyCore_Glass", new Color(0.2f, 0.7f, 1f, 0.16f));
            m.DataBody = Std("Obj_DataModule_Body", new Color(0.07f, 0.075f, 0.10f), 0.6f, 0.5f);
            m.DataTrim = Std("Obj_DataModule_Trim", new Color(0.18f, 0.2f, 0.25f), 0.9f, 0.55f);
            m.DataHolo = Additive("Obj_DataModule_Hologram", new Color(0f, 0.7f, 1f, 0.32f));
            m.CanBody = Std("Obj_Canister_Body", new Color(0.62f, 0.64f, 0.68f), 0.95f, 0.75f);
            m.CanDark = Std("Obj_Canister_Cap", new Color(0.14f, 0.15f, 0.18f), 0.9f, 0.5f);
            m.CanGlass = Glass("Obj_Canister_Glass", new Color(0.35f, 0.9f, 0.6f, 0.22f));
            m.CanGlow = Additive("Obj_Canister_SpecimenGlow", new Color(0.1f, 0.8f, 0.35f, 0.22f));
            m.CrysTrim = Std("Obj_Crystal_Frame", new Color(0.16f, 0.15f, 0.22f), 0.95f, 0.65f);
            m.CrysGlass = Glass("Obj_Crystal_Glass", new Color(0.6f, 0.25f, 1f, 0.45f));
            m.CrysCore = Glow("Obj_Crystal_Core", new Color(0f, 0.9f, 1f), 2.6f);
            m.CrysBase = Std("Obj_Crystal_Base", new Color(0.04f, 0.03f, 0.07f), 0.8f, 0.7f);
            m.CrysMagenta = Glow("Obj_Crystal_Magenta", new Color(0.95f, 0.2f, 1f), 2.4f);
            m.CrysViolet = Glow("Obj_Crystal_Violet", new Color(0.5f, 0.3f, 1f), 2.2f);
            m.CrysHalo = Additive("Obj_Crystal_Halo", new Color(0.7f, 0.2f, 1f, 0.28f));
            m.PowBody = Std("Obj_PowerCell_Body", new Color(0.22f, 0.10f, 0.03f), 0.5f, 0.45f);
            m.PowTrim = Std("Obj_PowerCell_Trim", new Color(0.10f, 0.10f, 0.12f), 0.85f, 0.5f);
            m.Brass = Std("Obj_PowerCell_Brass", new Color(0.6f, 0.45f, 0.15f), 0.95f, 0.7f);
            m.RobBody = Std("Obj_Robot_Body", new Color(0.75f, 0.77f, 0.80f), 0.7f, 0.6f);
            m.RobJoint = Std("Obj_Robot_Joint", new Color(0.13f, 0.14f, 0.17f), 0.9f, 0.5f);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }

        static Material Std(string name, Color albedo, float metallic, float smoothness, Color? emission = null, float intensity = 1f)
        {
            Material mat = LoadOrCreate(name, Shader.Find("Standard"));
            mat.SetColor("_Color", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value * intensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Emissive material: a dark body whose colour comes from the emission. Uses the built-in Standard shader; bloom is NOT assumed.</summary>
        static Material Glow(string name, Color emission, float intensity)
        {
            return Std(name, new Color(emission.r * 0.08f, emission.g * 0.08f, emission.b * 0.08f), 0.2f, 0.6f, emission, intensity);
        }

        /// <summary>Tinted see-through Standard material (Transparent mode, premultiplied alpha). The tint cue keeps its alpha. Shape never depends on it:
        /// every glass part has an opaque frame, rod or specimen that carries the silhouette.</summary>
        static Material Glass(string name, Color rgba)
        {
            Material mat = LoadOrCreate(name, Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.SetColor("_Color", rgba);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.9f);
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Soft additive glow using the built-in legacy particle shader. Its colour property is _TintColor, so SpatialTarget's
        /// focus/selected tint (which writes _Color) does not flatten it, and it needs no transparent Standard material.</summary>
        static Material Additive(string name, Color tint)
        {
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            Material mat = LoadOrCreate(name, s);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tint);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- primitive helpers (all colliders removed unless asked)

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool shadows = true, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            if (!keepCollider) { Collider c = go.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null, bool shadows = true)
        { return Prim(PrimitiveType.Cube, name, parent, pos, size, mat, rot, shadows); }

        static GameObject Cyl(string name, Transform parent, Vector3 pos, float radius, float height, Material mat, Quaternion? rot = null, bool shadows = true)
        { return Prim(PrimitiveType.Cylinder, name, parent, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat, rot, shadows); }

        static GameObject Sph(string name, Transform parent, Vector3 pos, float radius, Material mat, bool shadows = true)
        { return Prim(PrimitiveType.Sphere, name, parent, pos, Vector3.one * (radius * 2f), mat, null, shadows); }

        static Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        // ---------------------------------------------------------------- environment

        struct Side
        {
            public string Name; public Vector3 N; public Vector3 T;   // N = unit normal pointing INTO the room, T = tangent along the wall
            public Side(string name, Vector3 n, Vector3 t) { Name = name; N = n; T = t; }
        }

        static readonly Side[] Sides =
        {
            new Side("North", new Vector3(0, 0, -1), new Vector3(1, 0, 0)),    // wall at +z, the wall the player faces at start
            new Side("South", new Vector3(0, 0, 1), new Vector3(-1, 0, 0)),    // wall at -z, behind the player
            new Side("East", new Vector3(-1, 0, 0), new Vector3(0, 0, -1)),    // wall at +x
            new Side("West", new Vector3(1, 0, 0), new Vector3(0, 0, 1)),      // wall at -x
        };

        static Vector3 WallPoint(Side s, float tOff, float y, float depth)
        { return -s.N * Inner + s.T * tOff + s.N * depth + Vector3.up * y; }

        /// <summary>Box mounted on a wall; size is (along wall, height, thickness into the room); tilt rotates about the wall's horizontal axis.</summary>
        static GameObject WallBox(string name, Transform parent, Side s, float tOff, float y, float depth, Vector3 size, Material mat, float tilt = 0f, bool shadows = false)
        {
            return Box(name, parent, WallPoint(s, tOff, y, depth), size, mat, Quaternion.LookRotation(s.N, Vector3.up) * Quaternion.Euler(tilt, 0f, 0f), shadows);
        }

        static void BuildEnvironment(Transform root, Mats m)
        {
            BuildFloor(Group("Floor", root), m);
            BuildWalls(Group("Walls", root), m);
            BuildCeiling(Group("Ceiling", root), m);
            BuildPlatform(Group("ResearchPlatform", root), m);
            BuildStations(Group("WallStations", root), m);
            BuildLights(Group("Lighting", root));

            // Batching only: no lightmap baking, no occlusion data, nothing that changes project lighting settings.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<UnityEngine.Light>() == null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        static void BuildFloor(Transform g, Mats m)
        {
            // A visual deck 12 mm above the existing Floor plane (its collider stays the floor the gaze system uses).
            Box("FloorDeck", g, new Vector3(0, 0.006f, 0), new Vector3(Inner * 2f, 0.012f, Inner * 2f), m.FloorDeck, null, false);
            float len = Inner * 2f - 0.2f;
            for (int i = -2; i <= 2; i++)
            {
                float o = i * 2.5f;
                Box("Seam_X" + i, g, new Vector3(o, 0.0135f, 0), new Vector3(0.025f, 0.003f, len), m.FloorSeam, null, false);
                Box("Seam_Z" + i, g, new Vector3(0, 0.0135f, o), new Vector3(len, 0.003f, 0.025f), m.FloorSeam, null, false);
            }
            // Magenta edge strips just inside the play area boundary.
            float e = 6.95f;
            Box("EdgeStrip_N", g, new Vector3(0, 0.0145f, e), new Vector3(13.9f, 0.003f, 0.05f), m.Magenta, null, false);
            Box("EdgeStrip_S", g, new Vector3(0, 0.0145f, -e), new Vector3(13.9f, 0.003f, 0.05f), m.Magenta, null, false);
            Box("EdgeStrip_E", g, new Vector3(e, 0.0145f, 0), new Vector3(0.05f, 0.003f, 13.9f), m.Magenta, null, false);
            Box("EdgeStrip_W", g, new Vector3(-e, 0.0145f, 0), new Vector3(0.05f, 0.003f, 13.9f), m.Magenta, null, false);
            // Floor guide ring through the pedestals (36 short segments).
            const int seg = 36;
            float rr = 3.6f;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 p = PlatformCenter + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * rr;
                p.y = 0.0145f;
                Box("GuideRing_" + i, g, p, new Vector3(0.04f, 0.003f, rr * Mathf.PI * 2f / seg * 1.03f), m.Cyan, Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), false);
            }
        }

        static void BuildWalls(Transform g, Mats m)
        {
            float cy = WallHeight * 0.5f;
            const float th = 0.3f;
            float span = Inner * 2f + th * 2f;
            // The colliders: one box per wall. These are the ONLY colliders in the environment (so the gaze ray can land on a wall and
            // stop there, and nothing decorative can ever be focused because none of it carries a SpatialTarget).
            Prim(PrimitiveType.Cube, "Wall_North", g, new Vector3(0, cy, Inner + th * 0.5f), new Vector3(span, WallHeight, th), m.WallBase, null, true, true);
            Prim(PrimitiveType.Cube, "Wall_South", g, new Vector3(0, cy, -Inner - th * 0.5f), new Vector3(span, WallHeight, th), m.WallBase, null, true, true);
            Prim(PrimitiveType.Cube, "Wall_East", g, new Vector3(Inner + th * 0.5f, cy, 0), new Vector3(th, WallHeight, Inner * 2f), m.WallBase, null, true, true);
            Prim(PrimitiveType.Cube, "Wall_West", g, new Vector3(-Inner - th * 0.5f, cy, 0), new Vector3(th, WallHeight, Inner * 2f), m.WallBase, null, true, true);

            float[] bays = { -5.1f, -1.7f, 1.7f, 5.1f };
            foreach (Side s in Sides)
            {
                Transform sg = Group("Panels_" + s.Name, g);
                foreach (float b in bays)
                {
                    WallBox("Panel", sg, s, b, 1.7f, 0.05f, new Vector3(3.0f, 2.4f, 0.10f), m.WallPanel);
                    WallBox("Recess", sg, s, b, 1.7f, 0.115f, new Vector3(2.6f, 2.0f, 0.03f), m.Trim);
                    WallBox("TopStrip", sg, s, b, 3.0f, 0.075f, new Vector3(3.0f, 0.05f, 0.04f), m.Cyan);
                    WallBox("LowerVent", sg, s, b, 0.75f, 0.06f, new Vector3(2.4f, 0.2f, 0.06f), m.Trim);
                }
                foreach (float d in new[] { -6.8f, -3.4f, 0f, 3.4f, 6.8f })
                    WallBox("Divider", sg, s, d, cy, 0.07f, new Vector3(0.14f, WallHeight, 0.14f), m.Trim);
                WallBox("Skirting", sg, s, 0f, 0.3f, 0.06f, new Vector3(13.6f, 0.05f, 0.04f), m.Magenta);
                WallBox("Cornice", sg, s, 0f, 4.2f, 0.08f, new Vector3(14.6f, 0.18f, 0.16f), m.Trim);
            }

            // Corner pillars (not colliders) with glow strips.
            Transform pg = Group("Pillars", g);
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    float px = ix * 7.15f, pz = iz * 7.15f;
                    Box("Pillar", pg, new Vector3(px, cy, pz), new Vector3(0.5f, WallHeight, 0.5f), m.Trim);
                    Material gm = (ix * iz > 0) ? m.Cyan : m.Magenta;
                    Box("PillarStrip_X", pg, new Vector3(px - ix * 0.26f, cy, pz), new Vector3(0.04f, 3.6f, 0.04f), gm, null, false);
                    Box("PillarStrip_Z", pg, new Vector3(px, cy, pz - iz * 0.26f), new Vector3(0.04f, 3.6f, 0.04f), gm, null, false);
                }
        }

        static void BuildCeiling(Transform g, Mats m)
        {
            // No collider: looking up never "hits" anything, so the gaze reticle simply hides.
            Box("CeilingSlab", g, new Vector3(0, WallHeight + 0.1f, 0), new Vector3(Inner * 2f + 0.6f, 0.2f, Inner * 2f + 0.6f), m.Ceiling, null, false);
            foreach (float o in new[] { -4.5f, 0f, 4.5f })
            {
                foreach (float q in new[] { -4.5f, 0f, 4.5f })
                    Box("LightPanel", g, new Vector3(o, WallHeight - 0.015f, q), new Vector3(1.4f, 0.03f, 1.4f), m.WhitePanel, null, false);
            }
            foreach (float o in new[] { -2.25f, 2.25f })
            {
                Box("Beam_X", g, new Vector3(0, WallHeight - 0.1f, o), new Vector3(14.6f, 0.2f, 0.25f), m.Trim, null, false);
                Box("Beam_Z", g, new Vector3(o, WallHeight - 0.1f, 0), new Vector3(0.25f, 0.2f, 14.6f), m.Trim, null, false);
                Box("BeamGlow_X", g, new Vector3(0, WallHeight - 0.205f, o), new Vector3(14.6f, 0.012f, 0.05f), m.Cyan, null, false);
                Box("BeamGlow_Z", g, new Vector3(o, WallHeight - 0.205f, 0), new Vector3(0.05f, 0.012f, 14.6f), m.Magenta, null, false);
            }
            // Glowing ring above the research platform.
            Cyl("PlatformCeilingRing", g, new Vector3(PlatformCenter.x, WallHeight - 0.02f, PlatformCenter.z), 2.3f, 0.015f, m.Cyan, null, false);
            Cyl("PlatformCeilingCore", g, new Vector3(PlatformCenter.x, WallHeight - 0.012f, PlatformCenter.z), 2.2f, 0.03f, m.Ceiling, null, false);
        }

        static void BuildPlatform(Transform g, Mats m)
        {
            Vector3 c = PlatformCenter;
            // Layered tiers: dark / glow / dark / glow / dark. All decorative: NO colliders, so nothing is in the way of a target.
            Cyl("Tier1", g, c + new Vector3(0, 0.040f, 0), 2.20f, 0.080f, m.Platform);
            Cyl("Tier1_Glow", g, c + new Vector3(0, 0.0875f, 0), 2.08f, 0.015f, m.Cyan, null, false);
            Cyl("Tier2", g, c + new Vector3(0, 0.1225f, 0), 2.00f, 0.055f, m.Platform);
            Cyl("Tier2_Glow", g, c + new Vector3(0, 0.1575f, 0), 1.65f, 0.015f, m.Magenta, null, false);
            Cyl("Tier3", g, c + new Vector3(0, 0.18f, 0), 1.58f, 0.04f, m.Platform);
            // Hexagonal inlay: six bars around a hexagon of circumradius 1.2.
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * 60f * Mathf.Deg2Rad, a1 = (i + 1) * 60f * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * 1.2f, p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * 1.2f;
                Vector3 mid = (p0 + p1) * 0.5f;
                float ang = Mathf.Atan2(p1.x - p0.x, p1.z - p0.z) * Mathf.Rad2Deg;
                Box("HexBar_" + i, g, c + mid + new Vector3(0, 0.204f, 0), new Vector3(0.05f, 0.008f, (p1 - p0).magnitude + 0.04f), m.Cyan, Quaternion.Euler(0, ang, 0), false);
            }
            Cyl("CenterInlay", g, c + new Vector3(0, 0.202f, 0), 0.55f, 0.006f, m.Trim, null, false);
            Cyl("CenterGlow", g, c + new Vector3(0, 0.206f, 0), 0.40f, 0.004f, m.Magenta, null, false);
            // Restrained warning accents on the lowest tier.
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Box("Warn_" + i, g, c + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 2.14f + new Vector3(0, 0.082f, 0), new Vector3(0.28f, 0.006f, 0.05f), m.Amber, Quaternion.Euler(0, a * Mathf.Rad2Deg + 90f, 0), false);
            }

            // Display pedestals for the six movable assets (decorative; colliders removed).
            Transform pg = Group("Pedestals", g);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = AssetFloorPosition(i);
                Cyl("Pedestal_" + i + "_Column", pg, p + new Vector3(0, 0.38f, 0), 0.20f, 0.76f, m.Platform);
                Cyl("Pedestal_" + i + "_Cap", pg, p + new Vector3(0, 0.80f, 0), 0.34f, 0.04f, m.Trim);
                Cyl("Pedestal_" + i + "_Ring", pg, p + new Vector3(0, 0.825f, 0), 0.30f, 0.008f, i % 2 == 0 ? m.Cyan : m.Magenta, null, false);
                Cyl("Pedestal_" + i + "_Pad", pg, p + new Vector3(0, 0.832f, 0), 0.25f, 0.008f, m.Trim, null, false);
                Cyl("Pedestal_" + i + "_Foot", pg, p + new Vector3(0, 0.02f, 0), 0.30f, 0.04f, m.Trim);
            }
        }

        static void BuildStations(Transform g, Mats m)
        {
            Side north = Sides[0], south = Sides[1], east = Sides[2], west = Sides[3];

            // North wall: large display + two wall consoles.
            BigScreen(g, north, 0f, 2.3f, 5.0f, 2.0f, m, m.Cyan);
            Console(g, north, -5.1f, m);
            Console(g, north, 5.1f, m);
            Radar(g, north, -1.7f, 0.95f, m);

            // East and west walls: a wide decorative screen and one console each.
            BigScreen(g, east, 0f, 2.3f, 3.2f, 1.4f, m, m.Green);
            Console(g, east, 5.1f, m);
            BigScreen(g, west, 0f, 2.3f, 3.2f, 1.4f, m, m.Magenta);
            Console(g, west, -5.1f, m);

            // South wall (behind the player): door frame with restrained hazard markings, two small screens, two consoles.
            WallBox("DoorFrame", g, south, 0f, 1.55f, 0.10f, new Vector3(2.6f, 3.1f, 0.12f), m.Trim);
            WallBox("DoorLeaf", g, south, 0f, 1.5f, 0.17f, new Vector3(2.1f, 3.0f, 0.04f), m.Hazard);
            WallBox("DoorSeam", g, south, 0f, 1.5f, 0.195f, new Vector3(0.04f, 3.0f, 0.01f), m.Cyan);
            for (int i = 0; i < 6; i++)
            {
                float t = -1.0f + i * 0.4f;
                WallBox("DoorHazard_" + i, g, south, t, 3.2f, 0.12f, new Vector3(0.2f, 0.08f, 0.02f), i % 2 == 0 ? m.Amber : m.Hazard);
            }
            BigScreen(g, south, -3.4f, 2.3f, 1.8f, 0.9f, m, m.Cyan);
            BigScreen(g, south, 3.4f, 2.3f, 1.8f, 0.9f, m, m.Magenta);
            Console(g, south, -5.1f, m);
            Console(g, south, 5.1f, m);
        }

        static void BigScreen(Transform g, Side s, float tOff, float y, float w, float h, Mats m, Material accent)
        {
            WallBox("ScreenFrame", g, s, tOff, y, 0.14f, new Vector3(w + 0.2f, h + 0.2f, 0.08f), m.Trim);
            WallBox("ScreenFace", g, s, tOff, y, 0.19f, new Vector3(w, h, 0.02f), m.ScreenDim);
            // Decorative readout bars (no text): a header bar plus rows of different lengths.
            WallBox("ScreenHeader", g, s, tOff, y + h * 0.5f - 0.12f, 0.205f, new Vector3(w * 0.9f, 0.05f, 0.008f), accent);
            float[] widths = { 0.7f, 0.45f, 0.58f, 0.3f, 0.52f };
            for (int i = 0; i < widths.Length; i++)
            {
                float yy = y + h * 0.5f - 0.35f - i * (h - 0.6f) / widths.Length;
                float ww = w * widths[i];
                WallBox("ScreenRow_" + i, g, s, tOff - (w * 0.9f - ww) * 0.5f, yy, 0.205f, new Vector3(ww, 0.05f, 0.008f), i == 2 ? m.Amber : accent);
            }
        }

        static void Radar(Transform g, Side s, float tOff, float y, Mats m)
        {
            // Disc axis points into the room: rotate the cylinder 90 degrees about X inside the wall-facing frame.
            Quaternion face = Quaternion.LookRotation(s.N, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, "RadarRim", g, WallPoint(s, tOff, y, 0.12f), new Vector3(1.0f, 0.01f, 1.0f), m.Cyan, face, false);
            Prim(PrimitiveType.Cylinder, "RadarFace", g, WallPoint(s, tOff, y, 0.14f), new Vector3(0.9f, 0.01f, 0.9f), m.ScreenDim, face, false);
            Prim(PrimitiveType.Cylinder, "RadarCenter", g, WallPoint(s, tOff, y, 0.155f), new Vector3(0.12f, 0.01f, 0.12f), m.Green, face, false);
        }

        static void Console(Transform g, Side s, float tOff, Mats m)
        {
            WallBox("ConsoleBody", g, s, tOff, 1.0f, 0.30f, new Vector3(1.8f, 0.9f, 0.5f), m.Trim, 0f, true);
            WallBox("ConsoleScreen", g, s, tOff, 1.28f, 0.50f, new Vector3(1.5f, 0.55f, 0.04f), m.ScreenDim, -35f);
            WallBox("ConsoleScreenBar", g, s, tOff, 1.34f, 0.53f, new Vector3(1.1f, 0.05f, 0.012f), m.Cyan, -35f);
            WallBox("ConsoleDeck", g, s, tOff, 0.9f, 0.56f, new Vector3(1.6f, 0.04f, 0.3f), m.WallPanel, 0f, true);
            Material[] cols = { m.Cyan, m.Green, m.Amber, m.Magenta, m.Cyan, m.Green };
            for (int i = 0; i < 6; i++)
                WallBox("ConsoleButton_" + i, g, s, tOff - 0.6f + i * 0.24f, 0.925f, 0.56f, new Vector3(0.10f, 0.025f, 0.06f), cols[i]);
            WallBox("ConsoleLegL", g, s, tOff - 0.8f, 0.45f, 0.20f, new Vector3(0.08f, 0.9f, 0.08f), m.Trim);
            WallBox("ConsoleLegR", g, s, tOff + 0.8f, 0.45f, 0.20f, new Vector3(0.08f, 0.9f, 0.08f), m.Trim);
        }

        // ---------------------------------------------------------------- lighting

        static void BuildLights(Transform g)
        {
            // No shadows. Quality levels in this project allow only a few per-pixel lights, so the important ones are marked Important
            // and the rest fall back to vertex/SH lighting; emissive materials carry the neon look. Bloom is NOT assumed.
            MakeLight(g, "PlatformSpot", LightType.Spot, new Vector3(PlatformCenter.x, 4.15f, PlatformCenter.z), new Vector3(90, 0, 0), new Color(0.85f, 0.95f, 1f), 3.2f, 8f, 80f, LightRenderMode.ForcePixel);
            MakeLight(g, "FillNearViewer", LightType.Point, new Vector3(0, 3.6f, 1.0f), Vector3.zero, new Color(0.7f, 0.85f, 1f), 0.7f, 9f, 0f, LightRenderMode.ForcePixel);
            MakeLight(g, "CyanCornerNE", LightType.Point, new Vector3(5.5f, 3.4f, 5.5f), Vector3.zero, new Color(0f, 0.85f, 1f), 1.0f, 10f, 0f, LightRenderMode.Auto);
            MakeLight(g, "MagentaCornerNW", LightType.Point, new Vector3(-5.5f, 3.4f, 5.5f), Vector3.zero, new Color(1f, 0.15f, 0.8f), 1.0f, 10f, 0f, LightRenderMode.Auto);
            MakeLight(g, "MagentaCornerSE", LightType.Point, new Vector3(5.5f, 3.4f, -5.5f), Vector3.zero, new Color(1f, 0.15f, 0.8f), 0.8f, 10f, 0f, LightRenderMode.Auto);
            MakeLight(g, "CyanCornerSW", LightType.Point, new Vector3(-5.5f, 3.4f, -5.5f), Vector3.zero, new Color(0f, 0.85f, 1f), 0.8f, 10f, 0f, LightRenderMode.Auto);
        }

        static void MakeLight(Transform parent, string name, LightType type, Vector3 pos, Vector3 euler, Color color, float intensity, float range, float spotAngle, LightRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            var l = go.AddComponent<UnityEngine.Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            if (type == LightType.Spot) l.spotAngle = spotAngle;
            l.shadows = LightShadows.None;
            l.renderMode = mode;
        }

        static void ApplyLabLighting()
        {
            // Dark cool ambient so the emissive neon reads; reflections toned down so the default sky does not wash the metal.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.14f, 0.22f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.09f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.05f);
            RenderSettings.reflectionIntensity = 0.35f;
        }

        // ---------------------------------------------------------------- movable assets

        // Arc angles (degrees around the platform centre) chosen so that, seen from the start camera at (0, 1.6, 0), every asset lies
        // OUTSIDE the view cones of the original targets: TargetA about -29..-21 deg, TargetB about -5..+5 deg, TargetC about +18..+25 deg
        // (camera azimuth), and clear of the distractor cube. Evenly spaced angles put two assets directly behind TargetA and TargetC.
        // Each angle was checked offline with rays to points across the asset's height and +/-0.3 m sideways, not only to its centre.
        // The right side between about +25 and +95 degrees is shadowed by TargetC and the distractor, so no asset is placed there.
        static readonly float[] AssetArcDegrees = { -100f, -75f, -20f, 20f, 95f, 110f };

        /// <summary>Position on the floor beneath asset i: six points on a 3.6 m arc around the platform (see AssetArcDegrees).</summary>
        static Vector3 AssetFloorPosition(int i)
        {
            float a = AssetArcDegrees[i] * Mathf.Deg2Rad;
            return PlatformCenter + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.6f;
        }

        static void BuildMovableAssets(Transform root, Mats m)
        {
            string[] names = { "energy core", "data module", "research canister", "crystal specimen", "power cell", "robot component" };
            System.Func<Transform, Mats, GameObject>[] builders =
            {
                BuildEnergyCore, BuildDataModule, BuildCanister, BuildCrystal, BuildPowerCell, BuildRobotComponent
            };
            for (int i = 0; i < 6; i++)
            {
                GameObject go = builders[i](root, m);
                Vector3 p = AssetFloorPosition(i);
                Vector3 toViewer = new Vector3(-p.x, 0f, -p.z);   // face the viewer standing at the origin
                go.transform.position = p;
                go.transform.rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
                // Hover just above the pedestal pad: lift so the lowest renderer is 0.12 m over the pedestal top.
                Bounds b = WorldBounds(go);
                go.transform.position += new Vector3(0f, PedestalTop + 0.12f - b.min.y, 0f);

                var st = go.AddComponent<SpatialTarget>();
                st.displayName = names[i];
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            for (int i = 0; i < rs.Length; i++)
            {
                if (i == 0) b = rs[0].bounds; else b.Encapsulate(rs[i].bounds);
            }
            return b;
        }

        static GameObject NewAsset(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // SpatialTarget now keeps each renderer's own base colour (and alpha) when it tints, so layered materials survive focus/selection.
        // Shaders with no _Color/_BaseColor (the additive glows use _TintColor) are never touched by the tint at all.

        static GameObject BuildEnergyCore(Transform parent, Mats m)
        {
            // Tall containment tower: flared base and cap, three struts, a glass tube around a glowing chamber, three concentric rings.
            GameObject root = NewAsset("Asset_EnergyCore", parent);
            Transform t = root.transform;
            Cyl("BasePlate", t, new Vector3(0, -0.25f, 0), 0.24f, 0.06f, m.CoreBody);
            Cyl("BaseFlare", t, new Vector3(0, -0.205f, 0), 0.18f, 0.03f, m.CoreTrim);
            Cyl("TopPlate", t, new Vector3(0, 0.25f, 0), 0.24f, 0.06f, m.CoreBody);
            Cyl("TopFlare", t, new Vector3(0, 0.205f, 0), 0.18f, 0.03f, m.CoreTrim);
            Cyl("GlassTube", t, new Vector3(0, 0, 0), 0.155f, 0.38f, m.CoreGlass, null, false);
            Sph("CoreChamber", t, Vector3.zero, 0.12f, m.CoreGlow, false);
            Sph("CoreInner", t, Vector3.zero, 0.065f, m.WhitePanel, false);
            Sph("CoreHalo", t, Vector3.zero, 0.19f, m.CoreHalo, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (30f + i * 120f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * 0.2f, 0, Mathf.Sin(a) * 0.2f);
                Box("Strut_" + i, t, p, new Vector3(0.035f, 0.5f, 0.035f), m.CoreBody);
                Box("StrutBracketTop_" + i, t, p + new Vector3(0, 0.2f, 0), new Vector3(0.07f, 0.04f, 0.07f), m.CoreTrim);
                Box("StrutBracketBottom_" + i, t, p + new Vector3(0, -0.2f, 0), new Vector3(0.07f, 0.04f, 0.07f), m.CoreTrim);
            }
            Cyl("RingUpper", t, new Vector3(0, 0.14f, 0), 0.21f, 0.012f, m.Cyan, null, false);
            Cyl("RingMiddle", t, new Vector3(0, 0, 0), 0.225f, 0.01f, m.Magenta, null, false);
            Cyl("RingLower", t, new Vector3(0, -0.14f, 0), 0.21f, 0.012f, m.Cyan, null, false);
            Cyl("TopLens", t, new Vector3(0, 0.285f, 0), 0.07f, 0.02f, m.Cyan, null, false);
            Cyl("BottomLens", t, new Vector3(0, -0.285f, 0), 0.07f, 0.02f, m.Cyan, null, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;   // magenta indicators on the top plate rim, between the struts
                Box("MagentaTick_" + i, t, new Vector3(Mathf.Cos(a) * 0.2f, 0.283f, Mathf.Sin(a) * 0.2f), new Vector3(0.04f, 0.012f, 0.025f), m.Magenta, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), false);
            }
            Box("WarnA", t, new Vector3(0.17f, -0.283f, 0), new Vector3(0.06f, 0.012f, 0.025f), m.Amber, null, false);
            Box("WarnB", t, new Vector3(-0.17f, -0.283f, 0), new Vector3(0.06f, 0.012f, 0.025f), m.Amber, null, false);
            var c = root.AddComponent<CapsuleCollider>();
            c.direction = 1; c.center = Vector3.zero; c.radius = 0.25f; c.height = 0.68f;
            return root;
        }

        static GameObject BuildDataModule(Transform parent, Mats m)
        {
            // Angular cartridge: layered shell, shoulder plates, ribbed sides, inset data face with a chevron, bottom connector, floating glyph.
            GameObject root = NewAsset("Asset_DataModule", parent);
            Transform t = root.transform;
            Box("Body", t, Vector3.zero, new Vector3(0.34f, 0.46f, 0.07f), m.DataBody);
            Box("BackPlate", t, new Vector3(0, 0, -0.04f), new Vector3(0.30f, 0.40f, 0.012f), m.DataTrim);
            Box("ShoulderL", t, new Vector3(-0.11f, 0.245f, 0), new Vector3(0.14f, 0.03f, 0.085f), m.DataTrim, Quaternion.Euler(0, 0, 18f));
            Box("ShoulderR", t, new Vector3(0.11f, 0.245f, 0), new Vector3(0.14f, 0.03f, 0.085f), m.DataTrim, Quaternion.Euler(0, 0, -18f));
            Box("TopBlock", t, new Vector3(0, 0.27f, 0), new Vector3(0.12f, 0.04f, 0.05f), m.DataTrim);
            for (int s = -1; s <= 1; s += 2)
            {
                Box("SidePanel_" + s, t, new Vector3(s * 0.18f, 0, 0), new Vector3(0.02f, 0.38f, 0.09f), m.DataTrim);
                for (int i = -1; i <= 1; i++)
                    Box("SideRib_" + s + "_" + i, t, new Vector3(s * 0.19f, i * 0.12f, 0), new Vector3(0.012f, 0.03f, 0.10f), m.DataBody);
            }
            Box("FaceFrame", t, new Vector3(0, -0.01f, 0.04f), new Vector3(0.29f, 0.36f, 0.012f), m.DataTrim);
            Box("FacePanel", t, new Vector3(0, -0.01f, 0.047f), new Vector3(0.25f, 0.32f, 0.01f), m.ScreenDim, null, false);
            Box("ChevronA", t, new Vector3(-0.10f, 0.13f, 0.054f), new Vector3(0.07f, 0.014f, 0.006f), m.Cyan, Quaternion.Euler(0, 0, 35f), false);
            Box("ChevronB", t, new Vector3(-0.10f, 0.105f, 0.054f), new Vector3(0.07f, 0.014f, 0.006f), m.Cyan, Quaternion.Euler(0, 0, -35f), false);
            Box("DataBarA", t, new Vector3(0.02f, 0.12f, 0.054f), new Vector3(0.14f, 0.018f, 0.006f), m.Cyan, null, false);
            Box("DataBarB", t, new Vector3(-0.01f, 0.07f, 0.054f), new Vector3(0.20f, 0.018f, 0.006f), m.Cyan, null, false);
            Box("DataBarC", t, new Vector3(-0.03f, 0.02f, 0.054f), new Vector3(0.16f, 0.018f, 0.006f), m.Green, null, false);
            float[] hs = { 0.03f, 0.06f, 0.045f, 0.08f, 0.055f };
            for (int i = 0; i < hs.Length; i++)
                Box("Graph_" + i, t, new Vector3(-0.08f + i * 0.04f, -0.12f + hs[i] * 0.5f, 0.054f), new Vector3(0.022f, hs[i], 0.006f), m.Cyan, null, false);
            Box("DotMagenta", t, new Vector3(0.09f, -0.09f, 0.054f), new Vector3(0.03f, 0.03f, 0.006f), m.Magenta, null, false);
            Box("DotAmber", t, new Vector3(0.09f, -0.13f, 0.054f), new Vector3(0.03f, 0.03f, 0.006f), m.Amber, null, false);
            Box("ConnectorHousing", t, new Vector3(0, -0.27f, 0), new Vector3(0.22f, 0.06f, 0.05f), m.DataTrim);
            for (int i = 0; i < 6; i++)
                Box("Pin_" + i, t, new Vector3(-0.1f + i * 0.04f, -0.305f, 0), new Vector3(0.018f, 0.025f, 0.012f), m.Amber, null, false);
            Box("HoloBeam", t, new Vector3(0, 0.31f, 0), new Vector3(0.06f, 0.1f, 0.03f), m.DataHolo, null, false);
            Box("HoloPlane", t, new Vector3(0, 0.41f, 0), new Vector3(0.24f, 0.14f, 0.004f), m.DataHolo, null, false);
            Box("HoloDiamond", t, new Vector3(0, 0.41f, 0.004f), new Vector3(0.07f, 0.07f, 0.07f), m.DataHolo, Quaternion.Euler(45f, 0, 45f), false);
            Box("HoloGlyphH", t, new Vector3(0, 0.41f, 0.006f), new Vector3(0.18f, 0.006f, 0.003f), m.Cyan, null, false);
            var c = root.AddComponent<BoxCollider>();
            c.center = new Vector3(0, 0.07f, 0); c.size = new Vector3(0.40f, 0.82f, 0.12f);
            return root;
        }

        static GameObject BuildCanister(Transform parent, Mats m)
        {
            // Lab storage canister: steel caps and rings, four bracket rods, a tinted glass chamber with a visible glowing specimen.
            GameObject root = NewAsset("Asset_ResearchCanister", parent);
            Transform t = root.transform;
            Cyl("CapTop", t, new Vector3(0, 0.205f, 0), 0.165f, 0.07f, m.CanDark);
            Cyl("CapBottom", t, new Vector3(0, -0.205f, 0), 0.165f, 0.07f, m.CanDark);
            Cyl("CapTopRim", t, new Vector3(0, 0.245f, 0), 0.175f, 0.015f, m.CanBody);
            Cyl("CapBottomRim", t, new Vector3(0, -0.245f, 0), 0.175f, 0.015f, m.CanBody);
            Cyl("GlassChamber", t, Vector3.zero, 0.125f, 0.34f, m.CanGlass, null, false);
            Cyl("SpecimenRod", t, Vector3.zero, 0.014f, 0.30f, m.Green, null, false);
            Sph("SpecimenA", t, new Vector3(0, 0.02f, 0), 0.06f, m.Green, false);
            Sph("SpecimenB", t, new Vector3(0.03f, -0.09f, 0.02f), 0.035f, m.Green, false);
            Sph("SpecimenC", t, new Vector3(-0.03f, 0.1f, -0.02f), 0.03f, m.Green, false);
            Cyl("SpecimenGlow", t, Vector3.zero, 0.1f, 0.3f, m.CanGlow, null, false);
            Cyl("RingUpper", t, new Vector3(0, 0.165f, 0), 0.135f, 0.014f, m.CanBody);
            Cyl("RingLower", t, new Vector3(0, -0.165f, 0), 0.135f, 0.014f, m.CanBody);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Box("BracketRod_" + i, t, new Vector3(Mathf.Cos(a) * 0.14f, 0, Mathf.Sin(a) * 0.14f), new Vector3(0.022f, 0.36f, 0.022f), m.CanBody);
            }
            Cyl("BandCyan", t, new Vector3(0, 0.226f, 0), 0.168f, 0.01f, m.Cyan, null, false);
            Box("LabelPlate", t, new Vector3(0, 0.205f, 0.162f), new Vector3(0.10f, 0.04f, 0.012f), m.CanBody);
            Box("LabelStripe", t, new Vector3(0, 0.205f, 0.170f), new Vector3(0.08f, 0.012f, 0.006f), m.Amber, null, false);
            Cyl("ValveStem", t, new Vector3(0, 0.3f, 0), 0.025f, 0.07f, m.CanDark);
            Cyl("ValveArm", t, new Vector3(0.05f, 0.34f, 0), 0.018f, 0.1f, m.CanBody, Quaternion.Euler(0, 0, 90f));
            Sph("ValveKnob", t, new Vector3(0.1f, 0.34f, 0), 0.03f, m.CanDark);
            Box("LugL", t, new Vector3(-0.185f, 0.205f, 0), new Vector3(0.04f, 0.04f, 0.05f), m.CanDark);
            Box("LugR", t, new Vector3(0.185f, 0.205f, 0), new Vector3(0.04f, 0.04f, 0.05f), m.CanDark);
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                Box("Foot_" + i, t, new Vector3(Mathf.Cos(a) * 0.12f, -0.27f, Mathf.Sin(a) * 0.12f), new Vector3(0.05f, 0.03f, 0.05f), m.CanDark);
            }
            var c = root.AddComponent<CapsuleCollider>();
            c.direction = 1; c.center = new Vector3(0, 0.04f, 0); c.radius = 0.185f; c.height = 0.76f;
            return root;
        }

        static GameObject BuildCrystal(Transform parent, Mats m)
        {
            // Faceted specimen in a cage: translucent violet main crystal with a cyan inner core, tilted emissive shards, stepped holder.
            GameObject root = NewAsset("Asset_CrystalSpecimen", parent);
            Transform t = root.transform;
            Cyl("ContainmentBase", t, new Vector3(0, -0.23f, 0), 0.18f, 0.06f, m.CrysBase);
            Cyl("BaseStep", t, new Vector3(0, -0.19f, 0), 0.14f, 0.03f, m.CrysTrim);
            Cyl("BaseRing", t, new Vector3(0, -0.172f, 0), 0.16f, 0.012f, m.Cyan, null, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box("CageProng_" + sx + "_" + sz, t, new Vector3(sx * 0.115f, -0.02f, sz * 0.115f), new Vector3(0.016f, 0.36f, 0.016f), m.CrysTrim, Quaternion.Euler(-sz * 8f, 0, sx * 8f));
            Cyl("CageRing", t, new Vector3(0, 0.15f, 0), 0.125f, 0.014f, m.CrysTrim);
            Crystal(t, "CrystalMain", new Vector3(0, 0.04f, 0), 0.15f, 1.9f, Vector3.zero, m.CrysGlass);
            Crystal(t, "CrystalCore", new Vector3(0, 0.04f, 0), 0.075f, 1.9f, new Vector3(0, 30f, 0), m.CrysCore);
            Crystal(t, "FacetCap", new Vector3(0, 0.205f, 0), 0.06f, 1.3f, new Vector3(0, 20f, 0), m.CrysMagenta);
            Crystal(t, "ShardA", new Vector3(0.09f, -0.06f, 0.03f), 0.08f, 1.5f, new Vector3(0, 20, -18), m.CrysViolet);
            Crystal(t, "ShardB", new Vector3(-0.08f, -0.07f, -0.05f), 0.07f, 1.5f, new Vector3(10, 60, 16), m.CrysViolet);
            Crystal(t, "ShardC", new Vector3(0.01f, -0.08f, 0.10f), 0.06f, 1.4f, new Vector3(-14, 10, 4), m.CrysMagenta);
            Sph("CrystalHalo", t, new Vector3(0, 0.04f, 0), 0.2f, m.CrysHalo, false);
            var c = root.AddComponent<CapsuleCollider>();
            c.direction = 1; c.center = new Vector3(0, 0.02f, 0); c.radius = 0.18f; c.height = 0.72f;
            return root;
        }

        /// <summary>A faceted shard: a cube standing on one corner, stretched vertically, so it reads as a crystal rather than a block.</summary>
        static void Crystal(Transform parent, string name, Vector3 pos, float side, float stretch, Vector3 tilt, Material mat)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pos;
            pivot.transform.localRotation = Quaternion.Euler(tilt);
            pivot.transform.localScale = new Vector3(1f, stretch, 1f);
            Box("Facets", pivot.transform, Vector3.zero, Vector3.one * side, mat, Quaternion.Euler(35.264f, 45f, 0f), false);
        }

        static GameObject BuildPowerCell(Transform parent, Mats m)
        {
            // A cell lying on its side (wide, low silhouette, unlike the tall items): barrel body, brass end caps, ring bands, cradle,
            // a positive post and a negative connector, a status strip, and an amber bolt marking on the front.
            GameObject root = NewAsset("Asset_PowerCell", parent);
            Transform t = root.transform;
            Quaternion alongX = Quaternion.Euler(0, 0, 90f);
            Cyl("Barrel", t, Vector3.zero, 0.095f, 0.30f, m.PowBody, alongX);
            Cyl("EndCapPos", t, new Vector3(0.19f, 0, 0), 0.105f, 0.06f, m.Brass, alongX);
            Cyl("EndCapNeg", t, new Vector3(-0.19f, 0, 0), 0.105f, 0.06f, m.Brass, alongX);
            Cyl("InnerCapPos", t, new Vector3(0.235f, 0, 0), 0.07f, 0.03f, m.PowTrim, alongX);
            Cyl("InnerCapNeg", t, new Vector3(-0.235f, 0, 0), 0.07f, 0.03f, m.PowTrim, alongX);
            Cyl("TerminalPost", t, new Vector3(0.27f, 0, 0), 0.03f, 0.04f, m.Brass, alongX);
            Cyl("TerminalTipPos", t, new Vector3(0.292f, 0, 0), 0.02f, 0.006f, m.Red, alongX, false);
            Box("ConnectorNeg", t, new Vector3(-0.27f, 0, 0), new Vector3(0.03f, 0.07f, 0.11f), m.Brass);
            Box("ConnectorTipNeg", t, new Vector3(-0.287f, 0, 0), new Vector3(0.006f, 0.05f, 0.09f), m.Cyan, null, false);
            for (int i = -1; i <= 1; i++)
                Cyl("RingBand_" + i, t, new Vector3(i * 0.08f, 0, 0), 0.102f, 0.016f, i == 0 ? m.Brass : m.PowTrim, alongX);
            Box("CradleRail", t, new Vector3(0, -0.125f, 0), new Vector3(0.36f, 0.02f, 0.13f), m.PowTrim);
            Box("CradleClampL", t, new Vector3(-0.1f, -0.085f, 0), new Vector3(0.04f, 0.07f, 0.17f), m.PowTrim);
            Box("CradleClampR", t, new Vector3(0.1f, -0.085f, 0), new Vector3(0.04f, 0.07f, 0.17f), m.PowTrim);
            Box("StatusHousing", t, new Vector3(0, 0.1f, 0), new Vector3(0.2f, 0.014f, 0.055f), m.ScreenDim, null, false);
            for (int i = 0; i < 5; i++)
                Box("ChargeBar_" + i, t, new Vector3(-0.08f + i * 0.04f, 0.11f, 0), new Vector3(0.03f, 0.012f, 0.035f), i == 4 ? m.Amber : m.Green, null, false);
            Box("BoltUpper", t, new Vector3(0.012f, 0.02f, 0.096f), new Vector3(0.07f, 0.014f, 0.008f), m.Amber, Quaternion.Euler(0, 0, -60f), false);
            Box("BoltLower", t, new Vector3(-0.012f, -0.02f, 0.096f), new Vector3(0.07f, 0.014f, 0.008f), m.Amber, Quaternion.Euler(0, 0, -60f), false);
            Box("BoltMiddle", t, new Vector3(0, 0, 0.096f), new Vector3(0.05f, 0.014f, 0.008f), m.Amber, null, false);
            var c = root.AddComponent<BoxCollider>();
            c.center = new Vector3(0, -0.01f, 0); c.size = new Vector3(0.60f, 0.28f, 0.22f);
            return root;
        }

        static GameObject BuildRobotComponent(Transform parent, Mats m)
        {
            // Jointed arm segment on a base: servo housing, two joints with LED rings, bracket plates, an actuator rod, a sensor head
            // with a lens, and a pin connector with a cable.
            GameObject root = NewAsset("Asset_RobotComponent", parent);
            Transform t = root.transform;
            Quaternion alongX = Quaternion.Euler(0, 0, 90f);
            Box("BasePlate", t, new Vector3(0, -0.30f, 0), new Vector3(0.26f, 0.03f, 0.20f), m.RobJoint);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cyl("Bolt_" + sx + "_" + sz, t, new Vector3(sx * 0.11f, -0.28f, sz * 0.08f), 0.014f, 0.012f, m.RobBody);
            Box("ServoHousing", t, new Vector3(0, -0.23f, 0), new Vector3(0.14f, 0.12f, 0.12f), m.RobBody);
            Box("ServoPlate", t, new Vector3(0, -0.165f, 0), new Vector3(0.15f, 0.02f, 0.13f), m.RobJoint);
            Cyl("Joint1", t, new Vector3(0, -0.12f, 0), 0.075f, 0.16f, m.RobJoint, alongX);
            Cyl("Joint1CapL", t, new Vector3(-0.085f, -0.12f, 0), 0.055f, 0.012f, m.RobBody, alongX);
            Cyl("Joint1CapR", t, new Vector3(0.085f, -0.12f, 0), 0.055f, 0.012f, m.RobBody, alongX);
            Cyl("Joint1Led", t, new Vector3(0.094f, -0.12f, 0), 0.04f, 0.006f, m.Cyan, alongX, false);
            Box("LowerArm", t, new Vector3(0.03f, 0.0f, 0), new Vector3(0.07f, 0.22f, 0.06f), m.RobBody, Quaternion.Euler(0, 0, -15f));
            Box("LowerPlateL", t, new Vector3(-0.005f, 0.0f, 0.036f), new Vector3(0.05f, 0.18f, 0.012f), m.RobJoint, Quaternion.Euler(0, 0, -15f));
            Box("LowerPlateR", t, new Vector3(-0.005f, 0.0f, -0.036f), new Vector3(0.05f, 0.18f, 0.012f), m.RobJoint, Quaternion.Euler(0, 0, -15f));
            Box("LowerArmStripe", t, new Vector3(0.036f, -0.05f, 0), new Vector3(0.074f, 0.016f, 0.064f), m.Amber, Quaternion.Euler(0, 0, -15f), false);
            Cyl("Elbow", t, new Vector3(0.07f, 0.11f, 0), 0.055f, 0.12f, m.RobJoint, alongX);
            Cyl("ElbowLed", t, new Vector3(0.132f, 0.11f, 0), 0.025f, 0.006f, m.Amber, alongX, false);
            Box("UpperArm", t, new Vector3(0.04f, 0.2f, 0), new Vector3(0.06f, 0.17f, 0.05f), m.RobBody, Quaternion.Euler(0, 0, 25f));
            Cyl("ActuatorRod", t, new Vector3(0.065f, 0.19f, 0.045f), 0.014f, 0.17f, m.RobBody, Quaternion.Euler(0, 0, 25f));
            Cyl("ActuatorHousing", t, new Vector3(0.03f, 0.13f, 0.045f), 0.022f, 0.08f, m.RobJoint, Quaternion.Euler(0, 0, 25f));
            Box("SensorHead", t, new Vector3(-0.02f, 0.32f, 0), new Vector3(0.16f, 0.09f, 0.11f), m.RobBody);
            Box("HeadPlate", t, new Vector3(-0.02f, 0.37f, 0), new Vector3(0.12f, 0.012f, 0.09f), m.RobJoint);
            Cyl("LensHousing", t, new Vector3(-0.02f, 0.32f, 0.075f), 0.03f, 0.04f, m.RobJoint, Quaternion.Euler(90f, 0, 0));
            Cyl("LensGlow", t, new Vector3(-0.02f, 0.32f, 0.097f), 0.018f, 0.008f, m.Red, Quaternion.Euler(90f, 0, 0), false);
            Box("LedA", t, new Vector3(-0.07f, 0.368f, 0.0f), new Vector3(0.02f, 0.01f, 0.02f), m.Green, null, false);
            Box("LedB", t, new Vector3(-0.03f, 0.368f, 0.0f), new Vector3(0.02f, 0.01f, 0.02f), m.Amber, null, false);
            Box("ConnectorHousing", t, new Vector3(0.14f, -0.23f, 0), new Vector3(0.06f, 0.05f, 0.08f), m.RobJoint);
            for (int i = -1; i <= 1; i++)
                Box("ConnectorPin_" + i, t, new Vector3(0.18f, -0.23f, i * 0.025f), new Vector3(0.03f, 0.01f, 0.01f), m.Amber, null, false);
            Cyl("CableLow", t, new Vector3(0.15f, -0.14f, -0.05f), 0.01f, 0.2f, m.RobJoint, Quaternion.Euler(0, 0, 12f));
            var c = root.AddComponent<BoxCollider>();
            c.center = new Vector3(0.03f, 0.03f, 0); c.size = new Vector3(0.40f, 0.72f, 0.24f);
            return root;
        }

        // ---------------------------------------------------------------- verification (Editor geometry/physics only)

        static readonly string[] OriginalTargets = { "TargetA_RedCube", "TargetB_BlueSphere", "TargetC_GreenCapsule" };

        /// <summary>Returns a list of problems; empty means every builder check passed. Public so the self-test or a person can call it.</summary>
        public static List<string> Verify(Scene scene, GameObject env, GameObject assets, GazeFocusSystem gaze, Camera cam)
        {
            var issues = new List<string>();
            if (gaze == null || cam == null) { issues.Add("GazeFocusSystem or Main Camera missing."); return issues; }

            // 1. Only real movable assets are targets; no decorative object carries SpatialTarget.
            if (env.GetComponentsInChildren<SpatialTarget>(true).Length != 0) issues.Add("A SpatialTarget exists under " + EnvRootName + " (decorative scenery must not be targetable).");
            var mine = assets.GetComponentsInChildren<SpatialTarget>(true);
            if (mine.Length != 6) issues.Add("Expected 6 SpatialTargets under " + AssetsRootName + ", found " + mine.Length + ".");
            var all = Object.FindObjectsByType<SpatialTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SpatialTarget st in all)
            {
                bool inMine = st.transform.IsChildOf(assets.transform);
                bool original = System.Array.IndexOf(OriginalTargets, st.gameObject.name) >= 0;
                if (!inMine && !original) issues.Add("Unexpected SpatialTarget on '" + st.gameObject.name + "'.");
            }
            if (all.Length != 9) issues.Add("Expected 9 SpatialTargets in the scene (3 original + 6 new), found " + all.Length + ".");

            // 2. Unique spoken names that cannot be confused with commands; each asset has a collider and a renderer.
            var seen = new HashSet<string>();
            foreach (SpatialTarget st in all)
            {
                string n = VoiceIntentParser.Normalize(st.DisplayName);
                if (!seen.Add(n)) issues.Add("Duplicate displayName '" + n + "'.");
            }
            foreach (SpatialTarget st in mine)
            {
                var parsed = VoiceIntentParser.Parse("select the " + st.displayName);
                if (parsed.Status != ParseStatus.Ok || parsed.Kind != IntentKind.Select || VoiceIntentParser.Normalize(parsed.TargetName) != VoiceIntentParser.Normalize(st.displayName))
                    issues.Add("'select the " + st.displayName + "' does not parse back to the same target name (" + parsed + ").");
                if (st.GetComponentInChildren<Collider>() == null) issues.Add(st.name + " has no collider.");
                if (st.GetComponentInChildren<Rigidbody>() != null) issues.Add(st.name + " has a Rigidbody (the existing pipeline moves by Transform).");
            }

            // 3. Only the four walls are colliders in the environment.
            foreach (Collider c in env.GetComponentsInChildren<Collider>(true))
                if (!c.gameObject.name.StartsWith("Wall_")) issues.Add("Unexpected collider on environment object '" + c.gameObject.name + "'.");

            // 4. Scene invariants: one camera, one listener, no EventSystem added, original objects untouched in count.
            if (Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1) issues.Add("Camera count changed.");
            if (Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1) issues.Add("AudioListener count changed.");
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1) issues.Add("XR Origin count changed.");

            // 5. Bounds: inside the play area and the room, clear of walls, ceiling and each other.
            Vector3 ac = gaze.arenaCenter;
            var boundsList = new List<KeyValuePair<string, Bounds>>();
            foreach (SpatialTarget st in all) boundsList.Add(new KeyValuePair<string, Bounds>(st.name, WorldBounds(st.gameObject)));
            foreach (SpatialTarget st in mine)
            {
                Bounds b = WorldBounds(st.gameObject);
                Vector3 p = st.transform.position;
                if (Mathf.Abs(p.x - ac.x) > gaze.arenaHalfExtent || Mathf.Abs(p.z - ac.z) > gaze.arenaHalfExtent) issues.Add(st.name + " starts outside the play area.");
                if (b.min.y < ac.y) issues.Add(st.name + " starts below the arena floor.");
                if (b.max.y > ac.y + gaze.maxHeightAboveFloor + 1.5f) issues.Add(st.name + " starts too high (" + b.max.y.ToString("0.00") + " m).");
                if (Mathf.Max(b.extents.x, b.extents.z) > 0.38f) issues.Add(st.name + " is wider than 0.76 m, so at the play-area edge it could reach a wall.");
                if (Mathf.Abs(b.center.x) + b.extents.x > Inner - 0.1f || Mathf.Abs(b.center.z) + b.extents.z > Inner - 0.1f) issues.Add(st.name + " touches a wall.");
                Vector3 camToAsset = b.center - cam.transform.position;
                if (camToAsset.magnitude > gaze.maxDistance) issues.Add(st.name + " is " + camToAsset.magnitude.ToString("0.0") + " m from the camera, beyond the gaze range.");
            }
            for (int i = 0; i < boundsList.Count; i++)
                for (int j = i + 1; j < boundsList.Count; j++)
                    if (boundsList[i].Value.Intersects(boundsList[j].Value)) issues.Add(boundsList[i].Key + " overlaps " + boundsList[j].Key + ".");

            // 6. Line of sight from the viewing camera, using the camera's real pose in the saved scene. For every target the NEAREST collider
            //    on the ray must be the target itself, tested at the collider's centre AND at four points inside it (left, right, upper, lower),
            //    so a target that is only partly hidden behind another is caught. A ray that misses entirely at an off-centre point is
            //    tolerated (a capsule does not fill its bounding box); a ray that lands on a DIFFERENT object is a failure.
            Physics.SyncTransforms();
            Vector3 origin = cam.transform.position;
            foreach (SpatialTarget st in all)
            {
                Collider col = st.GetComponentInChildren<Collider>();
                if (col == null) continue;   // reported above for the new assets
                Bounds cb = col.bounds;
                Vector3 centre = cb.center;
                Vector3 toC = (centre - origin).normalized;
                Vector3 lateral = Vector3.Cross(Vector3.up, toC);
                if (lateral.sqrMagnitude < 1e-4f) lateral = Vector3.right;
                lateral.Normalize();
                float hx = Mathf.Max(cb.extents.x, cb.extents.z) * 0.6f, hy = cb.extents.y * 0.6f;
                Vector3[] samples = { centre, centre - lateral * hx, centre + lateral * hx, centre + Vector3.up * hy, centre - Vector3.up * hy };
                string[] labels = { "centre", "left", "right", "upper", "lower" };
                for (int k = 0; k < samples.Length; k++)
                {
                    Vector3 dir = (samples[k] - origin).normalized;
                    RaycastHit[] hits = Physics.RaycastAll(new Ray(origin, dir), gaze.maxDistance, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.MaxValue; Collider nearest = null;
                    foreach (RaycastHit h in hits) if (h.distance < best) { best = h.distance; nearest = h.collider; }
                    if (nearest == null) { if (k == 0) issues.Add(st.name + ": the ray to its centre hits nothing (collider does not cover its centre)."); }
                    else if (nearest.GetComponentInParent<SpatialTarget>() != st) issues.Add(st.name + " (" + labels[k] + "): the nearest collider on the way is '" + nearest.name + "' (blocked or wrong collider).");
                }
            }
            return issues;
        }
    }
}
#endif
