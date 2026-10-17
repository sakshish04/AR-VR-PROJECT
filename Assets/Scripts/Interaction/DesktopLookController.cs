using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Desktop stand-in for head gaze: rotates this transform (the camera) with right-mouse drag or the arrow keys.
    /// It only rotates; it never moves the camera. It switches itself off when an XR device is active so it cannot
    /// fight the headset. Simulated input only; not part of any XR validation.
    /// </summary>
    public class DesktopLookController : MonoBehaviour
    {
        public float mouseDegreesPerPixel = 0.15f;
        public float keyDegreesPerSecond = 60f;
        public float maxPitch = 80f;

        float yaw, pitch;
        bool initialized;

        void Start() { Init(); }

        void Init()
        {
            if (initialized) return;
            initialized = true;
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            Init();
            float dYaw = 0f, dPitch = 0f;
            if (Input.GetMouseButton(1))
            {
                dYaw += Input.GetAxis("Mouse X") * mouseDegreesPerPixel * 10f;
                dPitch -= Input.GetAxis("Mouse Y") * mouseDegreesPerPixel * 10f;
            }
            float k = keyDegreesPerSecond * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.LeftArrow)) dYaw -= k;
            if (Input.GetKey(KeyCode.RightArrow)) dYaw += k;
            if (Input.GetKey(KeyCode.UpArrow)) dPitch -= k;
            if (Input.GetKey(KeyCode.DownArrow)) dPitch += k;
            if (dYaw == 0f && dPitch == 0f) return;
            yaw += dYaw;
            pitch = Mathf.Clamp(pitch + dPitch, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
#endif
        }
    }
}
