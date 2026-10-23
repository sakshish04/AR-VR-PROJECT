#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Marks every Meta Project Setup Tool task on the WebGL tab as "Ignored" (the tool's own three-dot > Ignore action).
    /// The project targets Android/Quest and Windows only; Meta XR does not support WebGL, so those WebGL tasks can never pass.
    /// Reflection only, so it compiles regardless of the Meta SDK version. Undo: three-dot menu > un-tick Ignore, or run with ignored=false.
    /// Menu: Group05 > VoiceVR > Ignore WebGL Project Setup Tasks
    /// </summary>
    public static class IgnoreWebGLProjectSetupTasks
    {
        [MenuItem("Group05/VoiceVR/Ignore WebGL Project Setup Tasks")]
        public static void Run() { Apply(true); }

        public static void Apply(bool ignored)
        {
            Type setup = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                setup = asm.GetType("OVRProjectSetup");
                if (setup != null) break;
            }
            if (setup == null) { Debug.LogError("[IgnoreWebGL] OVRProjectSetup not found."); return; }

            var getTasks = setup.GetMethod("GetTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var tasks = (IEnumerable)getTasks.Invoke(null, new object[] { BuildTargetGroup.WebGL });
            int n = 0;
            foreach (var t in tasks)
            {
                var m = t.GetType().GetMethod("SetIgnored", BindingFlags.Instance | BindingFlags.Public);
                if (m == null) continue;
                m.Invoke(t, new object[] { BuildTargetGroup.WebGL, ignored });
                n++;
            }
            Debug.Log("[IgnoreWebGL] ignored=" + ignored + " on " + n + " WebGL task(s).");
        }
    }
}
#endif
