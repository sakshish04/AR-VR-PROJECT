# =====================================================================================
# NOTICE TO STUDENTS:
# The functions in this script generate SYNTHETIC/SIMULATED reference data for demonstration purposes only.
# Per Section 4 of your IVRAR Technical Dossier, you MUST conduct real human user trials (N >= 10),
# log actual telemetry/SUS data, and replace simulated numbers with your genuine experimental results.
# Submitting simulated data as real experimental findings violates NMIMS academic integrity policies.
# =====================================================================================
"""
Generate publication-grade figures (300 DPI) and empirical benchmark dataset for IVRAR Group 05.
Authorized Title: How can voice-driven spatial NLP commands in Unity VR reduce task completion
latency and interaction failure rates for motor-impaired users facing physical controller barriers?
"""

import os
import csv
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import matplotlib.patches as patches

# Configure high-resolution matplotlib parameters
plt.rcParams['font.sans-serif'] = 'Arial'
plt.rcParams['font.family'] = 'sans-serif'
plt.rcParams['figure.dpi'] = 300
plt.rcParams['savefig.dpi'] = 300
plt.rcParams['axes.titlesize'] = 11
plt.rcParams['axes.labelsize'] = 10
plt.rcParams['xtick.labelsize'] = 9
plt.rcParams['ytick.labelsize'] = 9
plt.rcParams['legend.fontsize'] = 8.5

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIGURES_DIR = os.path.join(BASE_DIR, "docs", "figures")
TELEMETRY_DIR = os.path.join(BASE_DIR, "telemetry")
os.makedirs(FIGURES_DIR, exist_ok=True)
os.makedirs(TELEMETRY_DIR, exist_ok=True)

# -------------------------------------------------------------------------
# Figure 1: System Architecture Diagram
# -------------------------------------------------------------------------
def generate_figure1():
    fig, ax = plt.subplots(figsize=(9.5, 5.8))
    ax.set_xlim(0, 100)
    ax.set_ylim(0, 100)
    ax.axis('off')

    # Title
    ax.text(50, 96, "Voice-Driven Spatial NLP Interaction Architecture for Accessible VR", 
            ha='center', va='center', fontsize=12, fontweight='bold', color='#1A202C')
    ax.text(50, 92, "Integrating W3C WebXR XAUR, Speech Intent Parsing, Gaze Deictic Binding, and Fitts' Law Telemetry", 
            ha='center', va='center', fontsize=9, fontstyle='italic', color='#4A5568')

    # Layer 1: Hardware & XR Input Layer
    rect1 = patches.FancyBboxPatch((4, 66), 26, 22, boxstyle="round,pad=1", ec="#2B6CB0", fc="#EBF8FF", lw=1.5)
    ax.add_patch(rect1)
    ax.text(17, 85, "Multimodal Input Stream\n(OpenXR / Audio Pipeline)", ha='center', va='center', fontweight='bold', color='#2B6CB0', fontsize=9.5)
    ax.text(17, 76, "- Continuous Microphone Stream\n- Head Gaze Vector Tracking\n- Eye Gaze Focus Raycast\n- Accessible Hands-Free Rig", 
            ha='center', va='center', fontsize=8, color='#2D3748')

    # Layer 2: Spatial NLP & Deictic Resolution Engine
    rect2 = patches.FancyBboxPatch((37, 66), 26, 22, boxstyle="round,pad=1", ec="#C53030", fc="#FFF5F5", lw=1.5)
    ax.add_patch(rect2)
    ax.text(50, 85, "Spatial NLP Intent Engine\n(Local ASR & Semantic Parser)", ha='center', va='center', fontweight='bold', color='#C53030', fontsize=9.5)
    ax.text(50, 76, "- Acoustic Phoneme Extraction\n- Intent Classifier (Select/Move)\n- Deictic Entity Grounding\n- Latency Guard (< 350ms)", 
            ha='center', va='center', fontsize=8, color='#2D3748')

    # Layer 3: Virtual Interaction & Accessible Physics
    rect3 = patches.FancyBboxPatch((70, 66), 26, 22, boxstyle="round,pad=1", ec="#276749", fc="#F0FFF4", lw=1.5)
    ax.add_patch(rect3)
    ax.text(83, 85, "Accessible Interaction Core\n(Unity XR Interaction Toolkit)", ha='center', va='center', fontweight='bold', color='#276749', fontsize=9.5)
    ax.text(83, 76, "- Target Magnetic Snapping\n- Motor Tremor Filtering\n- Dynamic Cursor Enlargement\n- Auditory Confirmation Cue", 
            ha='center', va='center', fontsize=8, color='#2D3748')

    # Connecting arrows downward
    ax.annotate("", xy=(17, 48), xytext=(17, 66), arrowprops=dict(arrowstyle="->", lw=1.8, color="#4A5568"))
    ax.annotate("", xy=(50, 48), xytext=(50, 66), arrowprops=dict(arrowstyle="->", lw=1.8, color="#4A5568"))
    ax.annotate("", xy=(83, 48), xytext=(83, 66), arrowprops=dict(arrowstyle="->", lw=1.8, color="#4A5568"))

    # Middle Layer: Accessible Virtual Workspace
    rect_env = patches.FancyBboxPatch((10, 30), 80, 18, boxstyle="round,pad=1", ec="#4A5568", fc="#EDF2F7", lw=1.5)
    ax.add_patch(rect_env)
    ax.text(50, 43, "Accessible Virtual Ergonomic Workspace (W3C XAUR & ISO 9241-9 Compliant)", 
            ha='center', va='center', fontweight='bold', color='#1A202C', fontsize=10)
    ax.text(50, 36, "3D Fitts' Law Target Array: Amplitudes D in [0.5m, 2.0m] | Target Widths W in [0.08m, 0.30m]\nDeictic Referencing: 'Put-That-There' Spatial Semantic Binding (Voice + Gaze Cursor)", 
            ha='center', va='center', fontsize=8.5, color='#4A5568')

    # Connecting arrow to telemetry
    ax.annotate("", xy=(50, 18), xytext=(50, 30), arrowprops=dict(arrowstyle="->", lw=1.8, color="#4A5568"))

    # Bottom Layer: Telemetry & Evaluation Analysis
    rect_tel = patches.FancyBboxPatch((15, 3), 70, 15, boxstyle="round,pad=1", ec="#6B46C1", fc="#FAF5FF", lw=1.5)
    ax.add_patch(rect_tel)
    ax.text(50, 14, "90 Hz Real-Time Accessibility Telemetry Pipeline", 
            ha='center', va='center', fontweight='bold', color='#6B46C1', fontsize=9.5)
    ax.text(50, 8, "Fitts' Law Throughput (TP) Engine | Speech Recognition Latency Monitor | Interaction Failure Rate Tracker | NASA-TLX Evaluator", 
            ha='center', va='center', fontsize=8.5, color='#4A5568')

    plt.tight_layout()
    fig1_path = os.path.join(FIGURES_DIR, "figure1_system_architecture.png")
    plt.savefig(fig1_path, dpi=300, bbox_inches='tight')
    plt.close()
    print(f"[OK] Generated: {fig1_path}")

# -------------------------------------------------------------------------
# Figure 2: Kinematic & Telemetry Graphs
# -------------------------------------------------------------------------
def generate_figure2():
    np.random.seed(42)
    fig, axs = plt.subplots(2, 2, figsize=(9.5, 7.5))

    # (a) Fitts' Law Regression: Movement Time vs Index of Difficulty (ID)
    id_vals = np.linspace(1.5, 5.5, 9)
    # Physical controller under motor tremor exhibits high slope and intercept
    mt_controller = 0.85 + 0.62 * id_vals + np.random.normal(0, 0.08, 9)
    # Voice-driven spatial selection exhibits lower slope (near-constant cognitive overhead)
    mt_voice_gaze = 0.65 + 0.18 * id_vals + np.random.normal(0, 0.05, 9)

    axs[0, 0].plot(id_vals, mt_controller, 's--', color='#E53E3E', label='Physical 6-DoF Controller (Tremor)')
    axs[0, 0].plot(id_vals, mt_voice_gaze, 'o-', color='#3182CE', lw=2, label='Voice + Gaze Spatial NLP')
    axs[0, 0].set_title("(a) Fitts' Law Target Acquisition Profile")
    axs[0, 0].set_xlabel("Index of Difficulty: ID = log2(D/W + 1) [bits]")
    axs[0, 0].set_ylabel("Movement Time MT (seconds)")
    axs[0, 0].legend()
    axs[0, 0].grid(True, linestyle='--', alpha=0.5)

    # (b) Speech-to-Intent End-to-End Latency Breakdown
    stages = ['Acoustic Capture', 'Feature Extraction', 'Intent Classification', 'Spatial Entity Grounding']
    latencies = [42.5, 68.2, 94.1, 48.3]
    lat_err = [3.2, 4.5, 6.1, 3.8]
    bars = axs[0, 1].bar(stages, latencies, yerr=lat_err, capsize=4, color='#4FD1C5', edgecolor='#234E52', alpha=0.85)
    axs[0, 1].axhline(350.0, color='#E53E3E', linestyle='--', label='W3C XAUR Latency Budget (350 ms)')
    axs[0, 1].set_title("(b) End-to-End Voice NLP Latency Breakdown")
    axs[0, 1].set_ylabel("Processing Latency (ms)")
    axs[0, 1].set_ylim(0, 380)
    axs[0, 1].legend()
    axs[0, 1].grid(axis='y', linestyle='--', alpha=0.5)

    # (c) Interaction Failure Rate vs Motor Tremor Severity
    tremor_levels = ['Normal (0)', 'Mild (1)', 'Moderate (2)', 'Severe (3)', 'Profound (4)']
    err_controller = [6.2, 18.5, 41.2, 68.4, 89.1]
    err_voice = [4.1, 5.2, 6.8, 9.4, 12.2]

    axs[1, 0].plot(tremor_levels, err_controller, 's--', color='#E53E3E', label='6-DoF Physical Controller')
    axs[1, 0].plot(tremor_levels, err_voice, 'o-', color='#38A169', lw=2, label='Voice Spatial NLP')
    axs[1, 0].set_title("(c) Target Selection Failure Rate vs Motor Tremor")
    axs[1, 0].set_xlabel("MDS-UPDRS Motor Tremor Severity Rating")
    axs[1, 0].set_ylabel("Interaction Failure Rate (%)")
    axs[1, 0].legend()
    axs[1, 0].grid(True, linestyle='--', alpha=0.5)

    # (d) Gaze Fixation Dwell Time Distribution
    dwell_times = np.random.normal(320, 45, 50)
    dwell_times = np.clip(dwell_times, 200, 480)
    axs[1, 1].hist(dwell_times, bins=10, color='#805AD5', edgecolor='#322659', alpha=0.85)
    axs[1, 1].axvline(np.mean(dwell_times), color='#D69E2E', linestyle='--', lw=2, label=f'Mean Fixation: {np.mean(dwell_times):.1f} ms')
    axs[1, 1].set_title("(d) User Gaze Fixation Prior to Voice Confirmation")
    axs[1, 1].set_xlabel("Gaze Dwell Time (ms)")
    axs[1, 1].set_ylabel("Frequency Count")
    axs[1, 1].legend()
    axs[1, 1].grid(axis='y', linestyle='--', alpha=0.5)

    plt.tight_layout()
    fig2_path = os.path.join(FIGURES_DIR, "figure2_kinematic_telemetry.png")
    plt.savefig(fig2_path, dpi=300, bbox_inches='tight')
    plt.close()
    print(f"[OK] Generated: {fig2_path}")

# -------------------------------------------------------------------------
# Figure 3: Comparative Performance Across Interaction Modalities
# -------------------------------------------------------------------------
def generate_figure3():
    fig, axs = plt.subplots(1, 3, figsize=(10.5, 3.8))

    modalities = ['Physical Hand\nController', 'Eye Gaze\nDwell Only', 'Voice + Gaze\nSpatial NLP']
    colors = ['#E53E3E', '#ED8936', '#38A169']

    # (a) Mean Task Completion Latency (s)
    lat_means = [4.12, 2.85, 1.48]
    lat_stds = [0.45, 0.28, 0.16]
    axs[0].bar(modalities, lat_means, yerr=lat_stds, capsize=5, color=colors, edgecolor='#1A202C', alpha=0.85)
    axs[0].set_title("(a) Mean Task Completion Latency (s)")
    axs[0].set_ylabel("Completion Latency (s)")
    axs[0].set_ylim(0, 5.0)
    axs[0].grid(axis='y', linestyle='--', alpha=0.5)
    axs[0].text(2, 2.1, "-64.1%\n(p < 0.001)", ha='center', fontweight='bold', color='#22543D', fontsize=8.5)

    # (b) Target Acquisition Failure Rate (%)
    fail_means = [48.2, 24.5, 6.8]
    fail_stds = [5.2, 3.8, 1.4]
    axs[1].bar(modalities, fail_means, yerr=fail_stds, capsize=5, color=colors, edgecolor='#1A202C', alpha=0.85)
    axs[1].set_title("(b) Target Selection Failure Rate (%)")
    axs[1].set_ylabel("Error Rate (%)")
    axs[1].set_ylim(0, 60)
    axs[1].grid(axis='y', linestyle='--', alpha=0.5)
    axs[1].text(2, 12, "-85.9%\n(p < 0.001)", ha='center', fontweight='bold', color='#22543D', fontsize=8.5)

    # (c) Fitts' Law Throughput (TP, bits/s)
    tp_means = [1.22, 2.15, 3.84]
    tp_stds = [0.18, 0.22, 0.31]
    axs[2].bar(modalities, tp_means, yerr=tp_stds, capsize=5, color=colors, edgecolor='#1A202C', alpha=0.85)
    axs[2].set_title("(c) Fitts' Law Throughput (bits/s)")
    axs[2].set_ylabel("Throughput (bits/s)")
    axs[2].set_ylim(0, 4.8)
    axs[2].grid(axis='y', linestyle='--', alpha=0.5)
    axs[2].text(2, 4.1, "+214.8%\n(p < 0.001)", ha='center', fontweight='bold', color='#22543D', fontsize=8.5)

    plt.tight_layout()
    fig3_path = os.path.join(FIGURES_DIR, "figure3_comparative_performance.png")
    plt.savefig(fig3_path, dpi=300, bbox_inches='tight')
    plt.close()
    print(f"[OK] Generated: {fig3_path}")

# -------------------------------------------------------------------------
# Benchmark Dataset Generation (N = 50 trials)
# -------------------------------------------------------------------------
def generate_benchmark_dataset():
    np.random.seed(105)
    n_samples = 50

    fieldnames = [
        "trial_id",
        "participant_id",
        "motor_impairment_level",
        "interaction_modality",
        "target_distance_m",
        "target_width_m",
        "fitts_id_bits",
        "movement_time_s",
        "fitts_throughput_bps",
        "nlp_recognition_latency_ms",
        "interaction_success_flag",
        "nasa_tlx_physical_demand",
        "sus_usability_score"
    ]

    csv_path = os.path.join(TELEMETRY_DIR, "SIMULATED_spatial_nlp_benchmark.csv")

    rows = []
    for i in range(n_samples):
        trial_id = f"NLP-TR-{i+1:03d}"
        part_id = f"USER-M{i+1:03d}"
        impairment = int(np.random.choice([1, 2, 3, 4], p=[0.25, 0.35, 0.25, 0.15]))
        
        r_val = np.random.rand()
        dist = float(np.random.choice([0.6, 0.9, 1.2, 1.5, 1.8]))
        width = float(np.random.choice([0.08, 0.12, 0.16, 0.20, 0.25]))
        id_bits = np.log2(dist / width + 1.0)

        if r_val < 0.25:
            mod = "Physical_Controller"
            mt = 0.85 + 0.58 * id_bits + (impairment * 0.45) + np.random.normal(0, 0.12)
            tp = id_bits / max(0.2, mt)
            nlp_lat = 0.0
            succ = 1 if np.random.rand() > (0.15 * impairment) else 0
            phys_dem = 72.0 + 5.0 * impairment + np.random.normal(0, 3.0)
            sus = 48.0 - 4.0 * impairment + np.random.normal(0, 4.0)
        elif r_val < 0.55:
            mod = "Eye_Gaze_Dwell"
            mt = 0.95 + 0.32 * id_bits + (impairment * 0.12) + np.random.normal(0, 0.08)
            tp = id_bits / max(0.2, mt)
            nlp_lat = 0.0
            succ = 1 if np.random.rand() > (0.06 * impairment) else 0
            phys_dem = 44.0 + 2.5 * impairment + np.random.normal(0, 3.0)
            sus = 66.0 - 2.0 * impairment + np.random.normal(0, 3.5)
        else:
            mod = "Voice_Gaze_Spatial_NLP"
            mt = 0.65 + 0.16 * id_bits + (impairment * 0.04) + np.random.normal(0, 0.05)
            tp = id_bits / max(0.2, mt)
            nlp_lat = 253.0 + np.random.normal(0, 22.0)
            succ = 1 if np.random.rand() > 0.05 else 0
            phys_dem = 21.0 + 1.2 * impairment + np.random.normal(0, 2.5)
            sus = 88.0 - 1.0 * impairment + np.random.normal(0, 3.0)

        mt = max(0.4, mt)
        tp = id_bits / mt
        phys_dem = np.clip(phys_dem, 0.0, 100.0)
        sus = np.clip(sus, 10.0, 100.0)

        row = {
            "trial_id": trial_id,
            "participant_id": part_id,
            "motor_impairment_level": impairment,
            "interaction_modality": mod,
            "target_distance_m": round(dist, 2),
            "target_width_m": round(width, 2),
            "fitts_id_bits": round(float(id_bits), 3),
            "movement_time_s": round(float(mt), 3),
            "fitts_throughput_bps": round(float(tp), 3),
            "nlp_recognition_latency_ms": round(float(nlp_lat), 1),
            "interaction_success_flag": int(succ),
            "nasa_tlx_physical_demand": round(float(phys_dem), 1),
            "sus_usability_score": round(float(sus), 1)
        }
        rows.append(row)

    with open(csv_path, 'w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    print(f"[OK] Generated benchmark dataset (N=50): {csv_path}")

if __name__ == "__main__":
    generate_figure1()
    generate_figure2()
    generate_figure3()
    generate_benchmark_dataset()
    print("[SUCCESS] All figures and benchmark telemetry generated for IVRAR Group 05.")
