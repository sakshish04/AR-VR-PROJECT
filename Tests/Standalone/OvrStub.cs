// Compile-check stand-in for the few Meta XR Core SDK (OVRInput) members used by Assets/Scripts/Trials/OvrControllerReader.cs.
// Used ONLY by compile_check_unity_scripts.ps1; never part of the Unity project.
using UnityEngine;
public static class OVRInput
{
    public enum Button { SecondaryIndexTrigger, SecondaryHandTrigger, PrimaryIndexTrigger, PrimaryHandTrigger }
    public enum RawButton { A, B, X, Y }
    public enum Controller { RTouch, LTouch }
    public static bool GetDown(Button b) { return false; }
    public static bool GetDown(RawButton b) { return false; }
    public static Vector3 GetLocalControllerPosition(Controller c) { return Vector3.zero; }
    public static Quaternion GetLocalControllerRotation(Controller c) { return Quaternion.identity; }
}

// Stand-ins for the Meta Voice SDK dictation types used by Assets/Scripts/Voice/QuestVoiceInput.cs (compile check only).
namespace Meta.WitAi.Dictation.Events
{
    public class DictationEvents
    {
        public UnityEngine.Events.UnityEvent<string> OnFullTranscription = new UnityEngine.Events.UnityEvent<string>();
        public UnityEngine.Events.UnityEvent<string> OnPartialTranscription = new UnityEngine.Events.UnityEvent<string>();
        public UnityEngine.Events.UnityEvent OnStoppedListening = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent<string, string> OnError = new UnityEngine.Events.UnityEvent<string, string>();
    }
}
namespace Oculus.Voice.Dictation
{
    public class AppDictationExperience : UnityEngine.MonoBehaviour
    {
        public Meta.WitAi.Dictation.Events.DictationEvents DictationEvents = new Meta.WitAi.Dictation.Events.DictationEvents();
        public bool Active { get { return false; } }
        public void Activate() { }
        public void Deactivate() { }
    }
}
