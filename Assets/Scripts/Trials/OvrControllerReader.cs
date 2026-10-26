using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Reads the Quest controllers through the Meta XR Core SDK (OVRInput), the same path the Interaction SDK uses. Unity's generic XR InputDevices list only the
    /// headset in the Meta XR Simulator (verified 2026-10-26: one device, "Head Tracking - OpenXR"), so the trial manager cannot see a controller press that way.
    /// Any easy input counts: index trigger, grip, or the A/B/X/Y face buttons, on either hand.
    /// </summary>
    public static class OvrControllerReader
    {
        /// <summary>True on the frame controller button A (right) is pressed: switches the headset microphone on and off.</summary>
        public static bool ToggleVoicePressed()
        {
            try { return OVRInput.GetDown(OVRInput.RawButton.A); }
            catch (System.Exception) { return false; }
        }

        /// <summary>True on the frame a button is pressed; the ray is the pressing controller's pointing ray in world space, or the camera's forward ray if its pose is unknown.</summary>
        public static bool TryGetPress(Camera cam, out Ray ray)
        {
            ray = new Ray(cam.transform.position, cam.transform.forward);
            try
            {
                bool right = OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger) || OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger)
                          || OVRInput.GetDown(OVRInput.RawButton.A) || OVRInput.GetDown(OVRInput.RawButton.B);
                bool left = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger)
                         || OVRInput.GetDown(OVRInput.RawButton.X) || OVRInput.GetDown(OVRInput.RawButton.Y);
                if (!right && !left) return false;

                OVRInput.Controller c = right ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
                Vector3 pos = OVRInput.GetLocalControllerPosition(c);
                Quaternion rot = OVRInput.GetLocalControllerRotation(c);
                Transform space = cam.transform.parent;
                if (pos.sqrMagnitude > 1e-6f)
                {
                    Vector3 wp = space != null ? space.TransformPoint(pos) : pos;
                    Quaternion wr = space != null ? space.rotation * rot : rot;
                    ray = new Ray(wp, wr * Vector3.forward);
                }
                return true;
            }
            catch (System.Exception)
            {
                return false;   // Meta XR Core SDK not active: the other input paths still work
            }
        }
    }
}
