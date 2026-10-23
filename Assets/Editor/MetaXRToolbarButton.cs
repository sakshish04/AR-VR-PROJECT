// Adds a "Meta XR" dropdown button to Unity's main toolbar (next to Play/Pause/Step).
// Requires Unity 6.3+ (MainToolbar API). Safe to delete; it only calls existing Meta menu items.
#if UNITY_EDITOR && UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Group05.EditorTools
{
    public static class MetaXRToolbarButton
    {
        const string ElementPath = "Group05/Meta XR SDK";

        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreateButton()
        {
            var icon = EditorGUIUtility.IconContent("BuildSettings.Android.Small").image as Texture2D;
            var content = new MainToolbarContent("Meta XR", icon, "Meta XR SDK tools");
            return new MainToolbarDropdown(content, ShowMenu);
        }

        static void ShowMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            AddItem(menu, "Building Blocks", "Meta/Tools/Building Blocks");
            AddItem(menu, "Project Setup Tool", "Meta/Tools/Project Setup Tool");
            AddItem(menu, "Meta XR Simulator (Activate/Deactivate)",
                "Meta/Meta XR Simulator/Activate",
                "Meta/Meta XR Simulator/Toggle",
                "Meta/Tools/Meta XR Simulator");
            menu.AddSeparator("");
            AddItem(menu, "Platform Settings", "Meta/Platform Settings");
            AddItem(menu, "XR Plug-in Management (Project Settings)", null);
            menu.DropDown(anchor);
        }

        static void AddItem(GenericMenu menu, string label, params string[] menuPaths)
        {
            menu.AddItem(new GUIContent(label), false, () =>
            {
                if (menuPaths == null || menuPaths.Length == 0 || menuPaths[0] == null)
                {
                    SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
                    return;
                }
                foreach (var path in menuPaths)
                    if (EditorApplication.ExecuteMenuItem(path)) return;
                Debug.LogWarning($"[MetaXRToolbar] Menu item not found for '{label}'. Tried: {string.Join(", ", menuPaths)}. " +
                                 "Check the Meta menu in the menu bar for the exact path in your SDK version.");
            });
        }
    }
}
#endif
