using UnityEngine;
using UnityEngine.UI;

namespace Group05.VoiceVR
{
    /// <summary>Click handler for the world-space Meta XR button (see MetaXRSceneButtonBuilder).</summary>
    public class MetaXRButtonHandler : MonoBehaviour
    {
        public Text label;
        int clicks;

        public void OnClicked()
        {
            clicks++;
            if (label != null) label.text = "Meta XR  x" + clicks;
            Debug.Log("[MetaXR] Button clicked (" + clicks + ")");
        }
    }
}
