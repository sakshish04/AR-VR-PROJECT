using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// FittsTargetTelemetryLogger manages procedural 3D target acquisition trials,
/// calculates Fitts' Law Index of Difficulty (ID) and Throughput (TP), and outputs 90 Hz CSV telemetry.
/// 
/// Course: IVRAR (Immersive Virtual, Real & Augmented Reality) - Group 05
/// Governing Standard: ISO 9241-9 / Shannon formulation ID = log2(D/W + 1)
/// </summary>
public class FittsTargetTelemetryLogger : MonoBehaviour
{
    [Header("Telemetry Configuration")]
    public string outputFilename = "fitts_spatial_nlp_telemetry.csv";
    public float samplingRateHz = 90f;

    [Header("Observed Components")]
    public SpatialVoiceIntentController voiceController;

    [Header("Current Target Parameters")]
    public float currentTargetDistance = 1.2f;
    public float currentTargetWidth = 0.15f;
    public float trialStartTime = 0f;

    // Student Technical Boundaries:
    // TODO [A057 - Sakshi Sharma]: Implement ISO 9241-9 procedural target positioning array in virtual 3D space.
    // TODO [I077 - Aryan Kanungo]: Compute Shannon Index of Difficulty (ID) and Fitts' Law Throughput (TP = ID / MT).

    private StreamWriter writer;
    private float sampleInterval;
    private float lastSampleTime;
    private bool isLogging = false;

    void Start()
    {
        sampleInterval = 1f / samplingRateHz;
        InitializeTelemetryLog();
        StartNewFittsTrial(1.2f, 0.15f);
    }

    void Update()
    {
        if (!isLogging || voiceController == null) return;

        if (Time.time - lastSampleTime >= sampleInterval)
        {
            RecordTelemetryFrame();
            lastSampleTime = Time.time;
        }
    }

    private void InitializeTelemetryLog()
    {
        try
        {
            string folderPath = Path.Combine(Application.dataPath, "..", "telemetry");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            string fullPath = Path.Combine(folderPath, outputFilename);

            writer = new StreamWriter(fullPath, false, Encoding.UTF8);
            writer.WriteLine("timestamp_s,target_dist_m,target_width_m,fitts_id_bits,elapsed_mt_s,nlp_latency_ms,has_gaze_focus");
            isLogging = true;
            Debug.Log($"[FittsTargetTelemetryLogger] Initialized telemetry writer at: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FittsTargetTelemetryLogger] Failed to initialize file writer: {ex.Message}");
            isLogging = false;
        }
    }

    public void StartNewFittsTrial(float distanceM, float widthM)
    {
        currentTargetDistance = distanceM;
        currentTargetWidth = widthM;
        trialStartTime = Time.time;

        // TODO [A057 - Sakshi Sharma]: Spawn target prefab at calculated 3D coordinates.
        Debug.Log($"[FittsTargetTelemetryLogger] Trial started. Distance: {distanceM:F2}m, Width: {widthM:F2}m");
    }

    private void RecordTelemetryFrame()
    {
        float id = Mathf.Log((currentTargetDistance / currentTargetWidth) + 1.0f, 2.0f);
        float elapsedMT = Time.time - trialStartTime;
        int hasGaze = (voiceController.focusedSpatialObject != null) ? 1 : 0;
        float nlpLat = voiceController.lastRecognitionLatencyMs;

        writer.WriteLine($"{Time.time:F3},{currentTargetDistance:F2},{currentTargetWidth:F2},{id:F3},{elapsedMT:F3},{nlpLat:F1},{hasGaze}");
    }

    /// <summary>
    /// Invoked when a target is successfully selected and acquired.
    /// </summary>
    public void OnTargetAcquired()
    {
        float movementTime = Time.time - trialStartTime;
        float id = Mathf.Log((currentTargetDistance / currentTargetWidth) + 1.0f, 2.0f);
        
        // TODO [I077 - Aryan Kanungo]: Compute Throughput TP = ID / MT in bits per second
        float throughput = id / Mathf.Max(0.1f, movementTime);
        Debug.Log($"[FittsTargetTelemetryLogger] Target Acquired! MT: {movementTime:F3}s, ID: {id:F2} bits, TP: {throughput:F2} bps");
    }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }
}
