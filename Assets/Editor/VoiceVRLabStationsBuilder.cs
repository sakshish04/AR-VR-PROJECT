#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Builds the lab task in the open scene: one designated dock for each of the nine items, in a row on the research platform
    /// (x -3.6 to 3.6 in 0.9 m steps, z 4.9, standing on the platform top at y 0.8), plus the <see cref="LabTaskManager"/>.
    /// Each dock is a plinth + glowing pad + floating label (<see cref="LabStation.BuildVisuals"/>), colour-coded and named after the item it takes.
    /// Idempotent: running it again rebuilds the docks in place (existing docks are reused, not duplicated). Materials are saved in Assets/Materials/Lab.
    /// Nothing else in the scene is touched. Undo (Ctrl+Z) works.
    /// Menu: Group05 > VoiceVR > Build Lab Docks
    /// </summary>
    public static class VoiceVRLabStationsBuilder
    {
        struct Dock
        {
            public string TargetObject, Name;
            public Color Accent;
            public Dock(string target, string name, Color accent) { TargetObject = target; Name = name; Accent = accent; }
        }

        // left to right as the player faces the row (+z)
        static readonly Dock[] Docks =
        {
            new Dock("TargetA_RedCube",          "RED BIN",           new Color(1.00f, 0.30f, 0.35f)),
            new Dock("TargetB_BlueSphere",       "BLUE BIN",          new Color(0.30f, 0.55f, 1.00f)),
            new Dock("TargetC_GreenCapsule",     "GREEN BIN",         new Color(0.30f, 0.95f, 0.50f)),
            new Dock("Asset_EnergyCore",         "REACTOR DOCK",      new Color(0.25f, 0.90f, 1.00f)),
            new Dock("Asset_DataModule",         "SERVER RACK",       new Color(0.55f, 0.60f, 1.00f)),
            new Dock("Asset_PowerCell",          "CHARGING STATION",  new Color(1.00f, 0.85f, 0.20f)),
            new Dock("Asset_RobotComponent",     "ASSEMBLY BAY",      new Color(1.00f, 0.60f, 0.20f)),
            new Dock("Asset_ResearchCanister",   "SPECIMEN STORAGE",  new Color(0.55f, 1.00f, 0.75f)),
            new Dock("Asset_CrystalSpecimen",    "ANALYSIS POD",      new Color(1.00f, 0.45f, 0.95f)),
        };

        const string MaterialDir = "Assets/Materials/Lab";
        const float RowZ = 4.9f, RowY = 0.8f, FirstX = -3.6f, Step = 0.9f;

        [MenuItem("Group05/VoiceVR/Build Lab Docks")]
        public static void Run()
        {
            string report = Apply();
            Debug.Log("[VoiceVRLabStationsBuilder] " + report);
        }

        public static string Apply()
        {
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
            Material plinth = LoadOrCreate("LabPlinth", "Standard", new Color(0.05f, 0.07f, 0.10f), true);
            Material ring = LoadOrCreate("LabRing", "Unlit/Color", new Color(0.2f, 0.7f, 0.9f), false);

            GameObject root = GameObject.Find("LabStations");
            if (root == null)
            {
                root = new GameObject("LabStations");
                Undo.RegisterCreatedObjectUndo(root, "Create LabStations");
            }

            var missing = new List<string>();
            int built = 0;
            for (int i = 0; i < Docks.Length; i++)
            {
                Dock d = Docks[i];
                GameObject targetGo = GameObject.Find(d.TargetObject);
                SpatialTarget target = targetGo != null ? targetGo.GetComponent<SpatialTarget>() : null;
                if (target == null) { missing.Add(d.TargetObject); continue; }

                string goName = "Dock_" + (i + 1) + "_" + d.Name.Replace(' ', '_');
                Transform existing = root.transform.Find(goName);
                GameObject go = existing != null ? existing.gameObject : new GameObject(goName);
                if (existing == null) { go.transform.SetParent(root.transform, false); Undo.RegisterCreatedObjectUndo(go, "Create " + goName); }
                go.transform.position = new Vector3(FirstX + i * Step, RowY, RowZ);
                go.transform.rotation = Quaternion.identity;

                LabStation st = go.GetComponent<LabStation>();
                if (st == null) st = Undo.AddComponent<LabStation>(go);
                st.target = target;
                st.stationName = d.Name;
                st.accent = d.Accent;
                st.number = i + 1;
                st.snapRadius = 0.40f;

                Material glow = LoadOrCreate("LabGlow_" + (i + 1), "Unlit/Color", d.Accent * 0.7f, false);
                st.BuildVisuals(plinth, glow, ring);
                EditorUtility.SetDirty(st);
                EditorUtility.SetDirty(go);
                built++;
            }

            if (UnityEngine.Object.FindFirstObjectByType<LabTaskManager>() == null)
            {
                var mgr = new GameObject("LabTaskManager");
                Undo.RegisterCreatedObjectUndo(mgr, "Create LabTaskManager");
                mgr.AddComponent<LabTaskManager>();
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();
            return "Docks built: " + built + " of " + Docks.Length + (missing.Count > 0 ? "; items not found in the scene: " + string.Join(", ", missing.ToArray()) : "") + ". Save the scene (Ctrl+S).";
        }

        static Material LoadOrCreate(string name, string shaderName, Color color, bool metallic)
        {
            string path = MaterialDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader sh = Shader.Find(shaderName) ?? Shader.Find("Unlit/Color");
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (metallic && m.HasProperty("_Metallic")) { m.SetFloat("_Metallic", 0.6f); m.SetFloat("_Glossiness", 0.55f); }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
#endif
