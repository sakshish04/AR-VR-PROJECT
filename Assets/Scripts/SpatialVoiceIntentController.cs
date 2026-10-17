using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpatialVoiceIntentController processes microphone audio input, recognizes spatial voice commands,
/// and grounds deictic references ("select that", "move here") via eye/head gaze raycasting.
/// 
/// Course: IVRAR (Immersive Virtual, Real & Augmented Reality) - Group 05
/// Governing Standard: W3C WebXR Accessibility User Requirements (XAUR)
/// </summary>
public class SpatialVoiceIntentController : MonoBehaviour
{
    public enum VoiceIntentType
    {
        None,
        SelectTarget,
        MoveToGaze,
        ReleaseTarget,
        CancelAction
    }

    [Header("Acoustic & Intent Settings")]
    [Tooltip("Target end-to-end speech recognition latency limit in milliseconds (XAUR benchmark: < 350ms).")]
    public float maxAllowedLatencyMs = 350f;

    [Tooltip("Minimum acoustic confidence score to trigger an action [0.0 to 1.0].")]
    public float confidenceThreshold = 0.75f;

    [Header("Current Interaction State")]
    public VoiceIntentType lastDetectedIntent = VoiceIntentType.None;
    public GameObject focusedSpatialObject;
    public float lastRecognitionLatencyMs = 0f;

    // Student Technical Boundaries:
    // TODO [I077 - Aryan Kanungo]: Implement local speech phoneme extraction, intent classification, and vocabulary parser.
    // TODO [A057 - Sakshi Sharma]: Implement OpenXR head/eye gaze raycasting to bind spatial deictic entity focus.

    private Transform cameraTransform;

    void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        InitializeSpeechRecognizer();
    }

    void Update()
    {
        UpdateSpatialGazeTarget();
    }

    /// <summary>
    /// Initializes on-device speech recognition engine.
    /// </summary>
    public void InitializeSpeechRecognizer()
    {
        // TODO [I077 - Aryan Kanungo]: Load phonetic grammar dictionary ("select", "grab", "push", "drop", "reset").
        Debug.Log("[SpatialVoiceIntentController] Speech-to-Intent NLP engine initialized.");
    }

    /// <summary>
    /// Raycasts from head/eye gaze to identify which virtual object the user is looking at.
    /// </summary>
    private void UpdateSpatialGazeTarget()
    {
        if (cameraTransform == null) return;

        Ray gazeRay = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;

        if (Physics.Raycast(gazeRay, out hit, 10f))
        {
            focusedSpatialObject = hit.collider.gameObject;
            // TODO [A057 - Sakshi Sharma]: Apply accessible outline highlight shader to indicate spatial focus.
        }
        else
        {
            focusedSpatialObject = null;
        }
    }

    /// <summary>
    /// Invoked by the ASR speech listener when a recognized phrase is parsed.
    /// </summary>
    public void OnVoicePhraseDetected(string recognizedText, float confidence, float latencyMs)
    {
        lastRecognitionLatencyMs = latencyMs;

        if (confidence < confidenceThreshold)
        {
            Debug.LogWarning($"[SpatialVoiceIntentController] Low confidence speech ignored: '{recognizedText}' ({confidence:F2})");
            return;
        }

        // TODO [I077 - Aryan Kanungo]: Map recognized text string to VoiceIntentType enum.
        ExecuteSpatialCommand(recognizedText);
    }

    /// <summary>
    /// Executes the spatial manipulation bound to the gaze-focused object.
    /// </summary>
    private void ExecuteSpatialCommand(string command)
    {
        if (focusedSpatialObject == null)
        {
            Debug.LogWarning("[SpatialVoiceIntentController] Voice command issued without an active gaze target.");
            return;
        }

        // TODO [A057 - Sakshi Sharma]: Trigger physical grab/move translation on focusedSpatialObject with soft magnetic snap.
        Debug.Log($"[SpatialVoiceIntentController] Executed '{command}' on target: {focusedSpatialObject.name}");
    }
}
