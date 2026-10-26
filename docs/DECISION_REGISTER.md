# VoiceVR — Decision Register, Approval Gates and Blockers

**Date:** 2026-10-17 · **Branch:** `dev/group-05-voicevr` (HEAD `3eda462`) · **Unity:** 6000.3.23f1

> **Update 2026-10-23 (Phase 04, research protocol):** rows D-05a and Q-01…Q-07 were added to §1, a status overlay for the study-design decisions was added as §1a, and the D-02 and D-05 rows now cite their dossier locations. **No decision was approved by anyone; nothing in §1a is CONFIRMED.** The full protocol draft is `docs/ROADMAP_PHASE04_RESEARCH_PROTOCOL.md`. Sections 2–7 (approval gates, G0/G2/G4/G5 results) are unchanged and describe the state as of 2026-10-17.

Nothing in the **Approval gates** section has been executed. Meta/Unity version-compatibility statements are **unverified**: the Meta documentation lookup (gate G0) has not been run, so every "proposed" value below is a candidate to verify, not a recommendation to apply blindly.

## 1. Open decisions (professor / team)

| ID | Question | Source | Why it matters | Current implementation | Alternatives | Decider |
|---|---|---|---|---|---|---|
| D-01 | Is Unity 6.3 acceptable vs the recommended 2022.3 LTS? | `toolchain_spec.md:6` (heading "Recommended"); guide §2.1; dossier §2 | Compatibility and grading | 6000.3.23f1 | Install 2022.3 and retarget | Professor |
| D-02 | Fitts formula: Shannon `log₂(D/W+1)` or the dossier's `log₂(2D/W)`? | Guide §1.1 (Shannon) vs dossier §1.1 (claim table, `TP = log2(2D/W) / MT`, faculty note "Not yet checked: verify") and dossier §2 ("Fundamental equations", `ID = log₂(2D / W)`); dossier locations verified in the file 2026-10-23 | Every ID/TP value | Shannon (provisional) | `log₂(2D/W)` | Professor |
| D-03 | Minimum-MT safeguard for throughput (0.1 s in starter code; 0.4 s in the figure generator)? | Logger; `generate_paper_figures.py` | Alters TP for fast trials | 0.1 s, raw MT always recorded beside it | None / other value | Professor |
| D-04 | Study design: conditions, within/between, order, trials per participant, counterbalancing | Dossier §4 (dropdown unselected); manuscript Table I | Validity of stats | Schema accepts `controller`, `gaze_voice` | Add gaze-dwell | Professor / team |
| D-05 | Participants: recruitment feasibility, motor-impaired or not, consent and ethics procedure | Dossier §4; title | Dossier §4: minimum N ≥ 10 real people (recommends N = 16–20), voluntary adults, written consent, anonymised IDs | None | — | Team + professor |
| D-06 | Is gaze-dwell a required condition? | Manuscript Table I vs dossier §2 | Scope | Excluded | Include | Professor |
| D-07 | Canonical telemetry schema (trial, frame, event) and `toolchain_spec.md`'s `trial_<id>.csv` | Spec vs schema | Compatibility with analysis | Proposed 25/12-column schemas | Rename to professor columns | Professor |
| D-08 | Is WER required, and who produces reference transcripts? | Dossier §2, §4 | Needs real sessions | `analysis/wer.py` | — | Professor |
| D-09 | Speech route: cloud (Wit.ai) allowed? offline mandatory? | README "offline" vs dossier "Meta Voice SDK (Wit.ai)" | Dependencies, privacy | Windows `KeywordRecognizer` only | Wit.ai; Android recognizer | Professor |
| D-10 | Quest model; PC `.exe` vs Quest `.apk` acceptable | Dossier §3 ("Quest .apk or PCVR .exe") | Build target | None | — | Team |
| D-11 | Latency definition (what start/stop events), < 350 ms | Guide §1.3 | Measurement validity | Recognizer-reported only, unvalidated | — | Professor |
| D-12 | Keep `Assets/index.html` (A-Frame)? | README/toolchain mention WebXR | Scope | Untouched | — | Professor |
| D-13 | Contents of the "automated compliance audit script"; missing `VoiceAccessibilityLogger.cs`, `assistive_vr_economics.py` | Guide Sprint 0/4; dossier §2 | Can't implement what's unspecified | None | — | Professor |
| D-14 | **Python ↔ Unity architecture.** No professor document specifies a Python bridge; no transport exists. Proposal for approval: loopback-only TCP (127.0.0.1, configurable port), one JSON object per line, flat fields `{v, id, modality, text}`, **disabled by default**; Python sends only the transcript; the C# `VoiceIntentParser` stays the single source of intent truth; duplicate-`id` and rate-limit protection; no code execution; mock sender for tests. No new packages (Python stdlib, .NET sockets). | User prompt | Architectural change | Nothing | Unity-only (no Python); named pipes; WebSocket | Team |
| D-15 | Interaction arbitration when gaze focus, controller hover and voice disagree (who owns the selection) | Not specified in any source | Prevents conflicting state | Gaze + typed/keyboard only | Single shared service with per-modality adapters | Team / professor |
| D-16 | Branch naming: `feat/i077-spatial-nlp-accessib` (guide) vs `…accessibility` (README/roster) vs actual `dev/group-05-voicevr` | Guide §2.2 vs README/roster | Dossier §3.1 counts | `dev/group-05-voicevr` | — | Team |
| D-17 | Target venue: TVCG/TACCESS/IEEE VR (guide) vs IndiaHCI/ACM ASSETS (dossier) | Guide vs dossier | Paper framing | — | — | Professor |
| D-18 | `SIMULATED_*.csv` in `telemetry/` are counted by dossier §3.1's `ls telemetry/*.csv` ("real participants") | Dossier §3.1 | Misreport risk | Untouched | Move/rename | Professor |
| D-05a | **Accessibility research question vs an able-bodied sample.** The faculty-authorised title (dossier §1) is about *motor-impaired* users; only able-bodied volunteers are available. Options: (a) recruit participants with motor impairment; (b) keep the framing, call the able-bodied study a preliminary step and limit every claim to able-bodied users; (c) reword the question to interaction performance in general | Dossier §1 (authorised title); Phase 04 §1 | Which claims the study may make; title and abstract wording | None | (a), (b), (c) | Professor |
| Q-01 | Primary outcome(s) of the study | Authorised title (task completion latency, interaction failure rate); dossier §4 (throughput, command latency, WER; SUS, NASA-TLX) | Which result the study is judged on | None | Proposed: co-primary latency and failure rate; throughput etc. secondary | Professor |
| Q-02 | Is the lab docking task (T2) needed, and how is it scored? | Phase 04 §3; `docs/LAB_DOCKS.md` | Scope and session length | Built in the scene, no telemetry | Drop it (T1 only); keep as secondary | Professor |
| Q-03 | Trial counts per condition, practice trials, per-trial time limit | Phase 04 §3.1 | Statistical precision, session length | None | Proposed: 9 D×W × 3 reps = 27 measured + 9 practice per condition; limit from the pilot | Team, after pilot |
| Q-04 | Seated vs standing; Meta XR Simulator vs a headset for the study | Phase 04 §5 | Validity, availability | Simulator only; no headset available | Seated; headset only if one becomes available | Team / professor |
| Q-05 | Compensation and recruitment route | Phase 04 §5 | Ethics, feasibility | None | — | Team + professor |
| Q-06 | Participant identity: pseudonymous IDs P01, P02, ...; the developers' own test sessions are never participant data | Dossier §4 (anonymised IDs); `TrialRecord` validation | Data integrity | Enforced in code for trial records (pseudonym pattern); gaze event log refuses `Real`; voice CSV has no origin column | Add an origin column to the voice CSV (Phase 05 schema decision) | Team (rule); professor if the policy changes |
| Q-07 | Sample size justification and the ethics / consent requirements | Dossier §4 (minimum N ≥ 10, recommends 16–20; written consent); Phase 04 §5 | Gates all recruitment | None | — | Professor |

## 1a. Phase 04 study-design decisions — status (2026-10-23)

Statuses: **CONFIRMED** = approved by the professor with date; **WORKING DECISION** = the project owner decided to proceed (dated), not approved by the professor; **PROPOSED** = the project owner's current proposal, not approved; **PENDING PROFESSOR APPROVAL** = cannot be treated as settled until the professor decides; **UNRESOLVED** = nobody has decided. **No row below is CONFIRMED.** Detail and reasoning: `docs/ROADMAP_PHASE04_RESEARCH_PROTOCOL.md` §11.

| ID | Position in the Phase 04 draft | Status |
|---|---|---|
| D-02 | **WORKING DECISION (owner, 2026-10-25, reaffirmed 2026-10-26): Shannon `log2(D/W+1)`**; dossier `log2(2D/W)` documented; professor confirmation pending. Data stores raw D, W and MT so either can be recomputed | WORKING DECISION; **conflict, PENDING PROFESSOR APPROVAL** |
| D-03 | keep raw MT; floor only for throughput | UNRESOLVED (suggestion) |
| D-04 | C1 controller vs C2 gaze+voice; every participant does both; AB/BA | PROPOSED |
| D-05 | mostly able-bodied volunteers plus participants with motor impairment where available; dossier N ≥ 10 (16–20 recommended); sample size and ethics not assumed | population: WORKING DECISION (2026-10-25); **professor approval and ethics PENDING** |
| D-05a | accessibility framing kept, every claim limited to the sample; motor-impaired results separate and descriptive (options in protocol §1) | WORKING DECISION (2026-10-25); **PENDING PROFESSOR APPROVAL** |
| D-06 | no gaze-dwell unless required | PROPOSED (professor may overrule) |
| D-08 | prompted-phrase accuracy; WER only with free speech | UNRESOLVED (suggestion) |
| D-09 | keep the offline keyword recogniser | UNRESOLVED (suggestion) |
| D-11 | recognizer-reported latency plus a stated limitation | UNRESOLVED (suggestion) |
| Q-01 | co-primary: task completion latency and failure rate | UNRESOLVED (suggestion) |
| Q-02 | T2 kept, provisional | PROPOSED, provisional |
| Q-03 | 27 measured + 9 practice trials per condition | UNRESOLVED (suggestion) |
| Q-04 | seated; simulator for development | UNRESOLVED (suggestion) |
| Q-05 | none | UNRESOLVED |
| Q-06 | pseudonymous IDs; team sessions never participant data | PROPOSED (enforced in code) |
| Q-07 | sample size: N = 10 (owner, 2026-10-26; dossier minimum, no power calculation); ethics route not assumed | sample size: WORKING DECISION; **professor approval and ethics PENDING** |

## 2. Approval gates (nothing below has been done)

Rollback for every package/settings gate: restore `Packages/manifest.json`, `Packages/packages-lock.json` and the affected `ProjectSettings/*` from a copy taken immediately before the change (the folders are untracked, so **take a copy first**; Git cannot restore them).

| Gate | Proposed change | Current → proposed | Compatibility / conflicts (UNVERIFIED) | Rollback |
|---|---|---|---|---|
| **G0** | Read-only Meta docs lookup: `npx -y metavr docs search …` | none → fetches the metavr CLI into the npm cache; no project change | Needed to verify G1–G7 facts | Clear npm cache entry |
| ↳ **G0 status** | **EXECUTED 2026-10-17 by web search/fetch of official pages, NOT via npx/metavr** (nothing downloaded or installed). Result: **PARTIALLY VERIFIED** — see §5 | | | |
| **G1** | Decide runtime handling of the already-active Meta XR Simulator v207.0 | Machine-wide `ActiveRuntime` = simulator v207.0 (verified); `PreviousActiveRuntime` = dangling v205.0 path (verified) | Affects every OpenXR app on this PC; `deactivate_simulator.ps1` would restore a non-existent v205 path — do not run it blindly | Manually set `ActiveRuntime` (admin, registry) |
| **G2** | Install Unity XR packages for Windows Standalone | XR Management, OpenXR, XR Interaction Toolkit, Input System: none installed → exact versions to be taken from G0 | Input System install usually prompts to change the active input handler; Unity 6.3 compatibility unverified | Restore manifest + lock |
| ↳ **G2 status** | **EXECUTED 2026-10-17 20:38–20:41, option A (packages only; handler unchanged).** Result: packages resolved and compiled; see §6. G3–G12 not started. | | | |
| **G3** | Meta XR Core / Interaction SDK packages, or a Meta scoped registry | none → only if G0 shows they are required | Possible overlap with XRI; "do not mix frameworks" | Restore manifest + lock |
| **G4** | XR Plug-in Management + OpenXR enabled for Windows Standalone; interaction profile(s) | No XR settings files exist → create | Profile names depend on simulator | Delete the generated settings (after copying originals) |
| ↳ **G4 status** | **APPROVED, NOT YET APPLIED — requires the Unity Editor UI (manual steps in §7.3).** No XR settings exist yet. | | | |
| **G5** | Active input handler | `0` (legacy only) → `Both` (2) | Keeps `DesktopVoiceInput`/`DesktopLookController` working; `New`-only would silently disable them | Restore `ProjectSettings.asset` |
| ↳ **G5 status** | **EXECUTED 2026-10-17 20:53–20:56: `activeInputHandler` 0 → 2.** One-line change; Unity batch run accepted it and all tests pass (§7.1–7.2). | | | |
| **G6** | Scene edits: reparent existing Main Camera under an XR Origin (same GameObject, same references); add XR Interaction Manager and left/right controllers with ray interactors | Plain camera → XR rig | Duplicate camera/AudioListener/EventSystem risk; `DesktopLookController` must stand down under XR | Scene is untracked: **copy `VoiceVR_Main.unity` first** |
| **G7** | Add Rigidbody + grab interactable to the red cube (then others) | Static colliders → grabbable | Physics interaction with floor/distractor; selection semantic vs gaze (D-15) | Revert scene copy |
| **G8** | Add `VoiceVR_Main` to Build Settings; any build | `m_Scenes: []` → one scene | Needed before any `.exe/.apk` | Restore `EditorBuildSettings.asset` |
| **G9** | Edit the professor controller to count only `SpatialTarget` as focus (smallest diff in `PHASE3_SCENE_AND_GAZE_REPORT.md` §6) | Any collider → valid targets only | Currently handled by a non-invasive `LateUpdate` bridge | `git checkout` the file |
| **G10** | Implement D-14 transport | — | See D-14 | Delete new files |
| **G11** | `git lfs pull` for the figures; `.gitattributes` | pointers → real PNGs | Network; LFS deferred by decision | — |
| **G12** | Commit / branch policy for untracked work (which paths to stage; whether to commit `Packages/` and `ProjectSettings/`) | all untracked | `.gitignore` is in place; **never `git add .`** | — |

## 3. Work deliberately NOT done this session, and why

| Item | Reason |
|---|---|
| New C# under `Assets/` (e.g. a Fitts target-array generator) | The Unity Editor is open (project lock held) and you may be mid-test; a script change can force a recompile. Safe to add once the Editor is closed and verified with a batch run. |
| Python ↔ Unity transport, router, receiver | Not in any professor document; architectural → D-14 / G10. |
| Any XR package, setting, scene or Build Settings change | Gates G2–G8. |
| Real-microphone testing, Play Mode visual checks | Need a person; no observations supplied yet. |
| Participant data, statistics on real data | None exists (D-05). |

## 4. Consolidated blockers and the next single action

**Blockers:** D-01…D-18, D-05a and Q-01…Q-07 above; gates G0–G12; no Play Mode observations yet; no participants; no Quest hardware.

**Independent work still available when the Editor is closed:** Fitts target-array layout generator (pure C# + tests, scene wiring behind G6); live trial recorder using `TrialRecord`; Unity batch re-verification after any C# change.

**Next single action:** send the Play Mode observations (checklist in `PHASE3_SCENE_AND_GAZE_REPORT.md` §5). In parallel, answer gate **G0** so the simulator integration facts can be verified before any package is installed.

> *Update 2026-10-17 (G0 executed):* G0 is done (§5). The next approval needed is **G2**, with the exact proposal in §5.8. The Play Mode observations are still outstanding.

---

## 5. G0 result — Meta XR Simulator v207.0 × Unity 6000.3.23f1 × OpenXR (executed 2026-10-17)

**Verdict: PARTIALLY VERIFIED.** The official sources support the Unity-native OpenXR route and name the Unity-released package versions for 6000.3, but **no Meta or Unity page states that Meta XR Simulator v207.x has been tested with Unity 6.3**. End-to-end compatibility can only be established by a runtime test after the packages are installed.

### 5.1 Method and limits of this evidence
- Tools: web search and page fetch only (read-only HTTPS requests). No `npx`, no metavr, no software or package downloaded or installed, no project file changed. The fetch tool summarises pages with a small model, and large registry JSON files were truncated at ~100,000 characters. Everything below is "as reported by those tools" and carries **moderate** confidence until seen in Unity's Package Manager.
- One fetched Meta page contained text addressed to AI agents about installing Meta tooling. It is not part of this request and was ignored.
- Page dates seen: Meta getting-started 2026-09-03, intro 2026-09-04, Unity-plugin page 2026-10-07, simulator download page "Updated Oct 14, 2026", release notes through 2026-10-17.

### 5.2 Sources
| Source | URL |
|---|---|
| Meta: Get Started with Meta XR Simulator | https://developers.meta.com/vr/documentation/unity/xrsim-getting-started/ |
| Meta: Meta XR Simulator Overview | https://developers.meta.com/vr/documentation/unity/xrsim-intro/ |
| Meta: XR Plugin Management for Meta VR | https://developers.meta.com/vr/documentation/unity/unity-xr-plugin/ |
| Meta: Release notes | https://developers.meta.com/vr/release-notes/ |
| Meta: Simulator (Windows) download page | https://developers.meta.com/vr/downloads/package/meta-xr-simulator-windows/ |
| Unity 6000.3 manual: OpenXR Plugin | https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.xr.openxr.html |
| Unity docs: OpenXR package release table (6000.3) | https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/packages-all/pack-safe/com-unity-xr-openxr |
| Unity 6000.3 manual: XR Interaction Toolkit | https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.xr.interaction.toolkit.html |
| Unity 6000.3 manual: Input System | https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.inputsystem.html |
| Unity 6000.3 manual: XR Plugin Management | https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.xr.management.html |
| OpenXR Plugin 1.16 manual | https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/index.html |
| Unity registry metadata (dependencies) | https://packages.unity.com/com.unity.xr.openxr · …/com.unity.xr.interaction.toolkit · …/com.unity.xr.management · …/com.unity.inputsystem |
| Seen in search only, not fetched | OpenXR 1.5 manual "project-configuration" (Play Mode OpenXR Runtime); a community forum thread (anecdotal) |

### 5.3 Verified facts (quoted or closely paraphrased from the pages above)
**Meta XR Simulator**
| # | Fact | Source |
|---|---|---|
| M1 | The standalone simulator is "a lightweight OpenXR runtime that runs on your computer"; Windows requirement "Windows 10+ (64-bit)"; installed by a standalone installer. | intro, getting-started |
| M2 | "The Meta XR Simulator does not require any additional packages in Unity if your project uses OpenXR." | getting-started |
| M3 | The Unity menu/toolbar activation (`Window > Meta > Meta XR Simulator > Activate`) comes from the Meta XR Core SDK; without that SDK use global activation. The old `com.meta.xr.simulator` Asset Store package is deprecated. | getting-started |
| M4 | Activating from Unity "sets this for the editor process only"; the simulator UI toggle shows the system-wide setting, so it may not show as active. | getting-started |
| M5 | Simulates a Meta Quest headset, controllers and hands. Input sources: keyboard and mouse, Xbox controller, Quest controllers via data forwarding. Left/right input sources are set under Inputs > Input settings. Default device Meta Quest 3; changing device requires restarting the app / Play Mode. | getting-started, intro |
| M6 | Device profiles: Quest 2, Quest Pro, Rift S, Quest 3 (default), Quest 3S, Meta VR Glasses. Meta VR Glasses "simulate eye gaze and hand tracking by default and present no controllers". | getting-started, intro |
| M7 | Apps must request OpenXR API version 1.0 through 1.1. Graphics on Windows: Vulkan, Direct3D 11, Direct3D 12. OpenGL / OpenGL ES not supported. Extension support varies by device profile. | intro |
| M8 | Only OpenXR-plugin floor stated: "Unity OpenXR Plugin v1.13.0 or later" — stated for **macOS**. "Older versions of the Meta Core SDK may not be fully compatible." | getting-started |
| M9 | Meta's Unity OpenXR guidance: "Unity OpenXR Plugin (recommended)", **1.15.1**, "Requires Unity 6 or later, Meta XR SDKs v74 or later"; install steps cite Unity 6000.0.66f2 or later (6.1+ recommended). Oculus XR Plugin "deprecated and scheduled for removal". | unity-xr-plugin |
| M10 | **No Meta page names Unity 6.3 or any maximum Unity version.** | all |

**Release status (official release pages)**
| # | Fact | Source |
|---|---|---|
| R1 | **207.0** (Sep 24, 2026): "introduces Look and Pinch, camera-driven hand tracking, and Meta VR Glasses support"; fixes Vulkan compositor readback and frame capture. Known issues: Vulkan validation layers cause a memory leak; VRS Recording does not capture interactions. Tagged for Unity and Unreal. | release notes |
| R2 | **207.1** (Windows, Oct 14, 2026): "Added AMD GPU support and grab/pinch from camera hand tracking"; fixes memory growth over long sessions, crashes on app exit, rendering / hand-tracking / input issues, and a Passthrough Camera Access white screen. Known issues: VRS Recording; **"If you update the Unity OpenXR package while the Editor is open, XR Simulator won't connect until you restart the Unity Editor."** | download page, release notes |
| R3 | Neither release states any Unity or OpenXR compatibility change. | both |

**Unity 6000.3 package versions**
| # | Fact | Source |
|---|---|---|
| U1 | OpenXR Plugin: "Package version **1.16.1** is released for Unity Editor version 6000.3." The release table also lists 1.17 (1.17.0, 1.17.1) and 1.18 (1.18.0) as "released" and 1.19 (pre-releases) as "compatible". 1.16 manual: minimum "2022.3 LTS+". | Unity OpenXR pages |
| U2 | XR Interaction Toolkit: "Package version **3.3.2** is released for Unity Editor version 6000.3." (3.4.x–3.6.x also exist in the registry.) | Unity XRI page, registry |
| U3 | Input System: "Package version **1.20.1** is released for Unity Editor version 6000.3." | Unity Input System page |
| U4 | XR Plugin Management: "Package version **4.6.1** is released for Unity Editor version 6000.3"; 4.7.0 also released. | Unity XR Management page |
| U5 | Dependencies reported by the registry (truncated, moderate confidence): OpenXR 1.16.1 → Input System 1.6.3, XR Core Utils 2.3.0, XR Management 4.4.0, Legacy Input Helpers 2.1.2; XRI 3.3.2 → Unity UI 2.0.0, Input System 1.8.1, XR Core Utils 2.4.0, Mathematics 1.2.6, built-in xr/physics/audio/imgui modules; XR Management 4.6.1 → XR Core Utils 2.2.1, Legacy Input Helpers 2.1.11. | registry |
| U6 | 1.16 manual lists Windows 64-bit with DX11 as the preferred graphics API for conformant runtimes; interaction profiles it names: Eye Gaze Interaction, HTC Vive, Khronos Simple Controller, Microsoft Motion Controller, **Oculus Touch Controller**, Valve Index. It does not mention Meta Quest Touch Pro/Plus profiles. Build-time validation reports errors/warnings; no "Fix All" mentioned. | OpenXR 1.16 manual |

### 5.4 Checked against this project and machine (read-only)
| Check | Result |
|---|---|
| Editor version vs Meta's stated floor (6000.0.66f2) | `6000.3.23f1` is newer → **satisfies** the stated floor |
| Installed XR / Input packages | **None** (`Packages/manifest.json`, last written 16:45:37, unchanged) |
| Graphics API | `m_BuildTargetGraphicsAPIs: []` (platform default, not an explicit choice). The running Editor reports **Direct3D 12 [level 12.2]** on an NVIDIA GeForce RTX 5050 Laptop GPU (window title `<DX12>`; an earlier session showed `<DX11>`). D3D11 and D3D12 are both listed as supported by the simulator (M7); Unity's OpenXR manual names DX11 as preferred (U6) |
| Simulator on this PC | Installed **207.0**; registered as the machine OpenXR runtime; not running |
| Newest official simulator | **207.1** (Windows, 2026-10-14). Not installed; not upgraded |
| Editor session | Main Editor PID 86232, started 19:59:09, scene `VoiceVR_Main` loaded, lock held |

### 5.5 v207.1 vs the installed 207.0
The official pages describe 207.1 as a **newer patch** (AMD GPU support, camera-hand grab/pinch, stability fixes). They contain **no change to Unity/OpenXR compatibility guidance** and no Unity version statement for either release. This machine has an NVIDIA GPU, so the AMD addition is irrelevant here. The stability fixes (memory growth over long sessions, exit crashes) could matter for repeated test sessions. **Nothing was upgraded.** Whether to install 207.1 is an optional, separate user action.

### 5.6 Reasonable inferences (NOT stated by any source)
| # | Inference | Basis |
|---|---|---|
| I1 | The Unity-native stack (XR Plugin Management + OpenXR + XR Interaction Toolkit + Input System) is the right integration; the Meta XR SDK packages (G3) are **not needed** for head pose + controller ray + grab. | M2 plus the simulator being a standard OpenXR runtime. No source mentions XRI with the simulator. |
| I2 | Candidate versions are the set Unity labels "released for 6000.3": **OpenXR 1.16.1, XR Plugin Management 4.6.1, XR Interaction Toolkit 3.3.2, Input System 1.20.1**. | U1–U4; Meta's 1.15.1 recommendation (M9) is older and tied to Meta SDK v74+; no Meta page rules out 1.16.1. |
| I3 | Head-pose gaze is plausible (simulated headset pose drives the camera). True eye gaze would need the Meta VR Glasses profile and Unity's Eye Gaze Interaction profile. | M5, M6, U6 |
| I4 | Simulated left/right controllers should surface through the Oculus Touch Controller profile. | M5, U6 |

### 5.7 Unresolved compatibility questions
1. No source tests Meta XR Simulator 207.x with Unity **6.3** specifically (M10). Only a runtime test can settle it.
2. OpenXR 1.16.1 (Unity-released for 6000.3) vs Meta's older recommended 1.15.1: not tested together; Meta's page may simply lag.
3. Registry-derived dependency numbers (U5) are from a truncated, summarised feed; one oddity (XRI 3.3.2 vs 3.4.1 minimum editor fields) suggests noise. Confirm in Package Manager before relying on it.
4. Which Unity OpenXR interaction profiles (Oculus Touch vs Meta Quest Touch Pro/Plus) the simulator exposes per device profile is not documented in the fetched pages.
5. The "Play Mode OpenXR Runtime" Editor setting was seen only in the 1.5 manual (search result), not on the 1.16 page. Whether it exists in 1.16+ is unverified; the default is the system active runtime (already the simulator, G1).
6. Whether D3D12 (current default here) or D3D11 should be forced for the simulator is not settled.
7. Eye-gaze support via the simulator in Unity is unverified.
8. Known issue R2: installing / updating the OpenXR package while the Editor is open can leave the simulator unable to connect until the Editor is restarted.

### 5.8 Does the evidence support proceeding to XR package installation?
**Yes, to G2 only, conditionally:** it supports a *test installation* of the Unity-native stack, because Meta says no extra Unity packages are required for OpenXR projects and Unity lists these versions as released for 6000.3. It does **not** guarantee end-to-end operation; that is to be established by a Play Mode run in the simulator.

**Exact proposal for G2 (needs your approval; nothing done):**
| Package | Proposed version | Basis |
|---|---|---|
| `com.unity.xr.management` | 4.6.1 | U4 |
| `com.unity.xr.openxr` | 1.16.1 | U1 |
| `com.unity.xr.interaction.toolkit` | 3.3.2 | U2 |
| `com.unity.inputsystem` | 1.20.1 | U3 |
No Meta XR SDK package (G3 not required per I1).

Conditions for G2: (a) copy `Packages/manifest.json`, `Packages/packages-lock.json` and `ProjectSettings/` first, because those folders are untracked and Git cannot restore them; (b) do the manifest change with the Unity Editor closed, then reopen it (R2); (c) expect a prompt to change the Active Input Handler and choose **Both**, not "New" only (otherwise `DesktopVoiceInput` and `DesktopLookController` stop working; G5); (d) G4 (enable OpenXR for Windows Standalone) and G6/G7 (scene) remain separate approvals; (e) rollback = restore the copied files.

### 5.9 Housekeeping finding during G0
A "1 file changed in the last 30 minutes" reading was investigated. It was `Assets/Editor/VoiceVRGazeSelfTests.cs` (19:49:30), my own approved self-test repair. A full hash comparison of the 78 files under `Assets/`, `Packages/` and `ProjectSettings/` against the baseline saved immediately after that repair shows **0 changed, 0 new, 0 missing**. No tracked file is modified, nothing is staged, the professor scripts are identical to the commit, the scene (19:43:36) and manifest (16:45:37) are unchanged.

---

## 6. G2 result — XR package installation, option A (executed 2026-10-17 20:38–20:41)

**Scope executed:** install the four approved packages only. The Active Input Handler was left unchanged. No XR/OpenXR project settings, scene, Build Settings or C# script was touched. G3–G12 were not started.

### 6.1 Preconditions and backup
- Before starting: no Unity process (only the idle `unity mcp` client), `Temp\UnityLockfile` not held, branch `dev/group-05-voicevr`, HEAD `3eda462`, 0 staged, 0 tracked-modified, handler `0`.
- Backup: `D:\arvr\Group_05_Sakshi_Aryan\.backups\g2_20261017-203854\` — 24 files (`Packages\manifest.json`, `Packages\packages-lock.json`, all 22 `ProjectSettings\*`), **0 SHA-256 mismatches**, hash list in `SHA256_manifest.csv`, folder created fresh (no overwrite). `.backups/` is now git-ignored (one line added to `.gitignore`).

### 6.2 Changes made
| File | Change |
|---|---|
| `Packages/manifest.json` | **+4 lines exactly** (diff vs backup verified): `com.unity.inputsystem 1.20.1`, `com.unity.xr.interaction.toolkit 3.3.2`, `com.unity.xr.management 4.6.1`, `com.unity.xr.openxr 1.16.1` |
| `Packages/packages-lock.json` | Rewritten by Unity: 37 → 45 entries; no pre-existing entry changed |
| `.gitignore` | + `/.backups/` |
| `Library/PackageCache/` | 8 new package folders (generated, git-ignored) |
| `.backups/g2_…` | new backup folder (git-ignored) |
| `telemetry/selftest/unity_gaze_selftest_20261017T151115Z.txt` | new self-test results (git-ignored) |

**Not changed (verified by SHA-256):** all 24 `ProjectSettings` files are identical to the backup (including `ProjectSettings.asset`: `activeInputHandler: 0`; `EditorBuildSettings`: `m_Scenes: []`); all `Assets/` files identical to the 19:50 baseline (scene still 19:43:36); professor files identical to commit `3eda462`.

### 6.3 Package resolution (from `packages-lock.json` and the Unity log)
| Package | Resolved | Role | Source |
|---|---|---|---|
| `com.unity.inputsystem` | **1.20.1** | direct | registry |
| `com.unity.xr.interaction.toolkit` | **3.3.2** | direct | registry |
| `com.unity.xr.management` | **4.6.1** | direct | registry |
| `com.unity.xr.openxr` | **1.16.1** | direct | registry |
| `com.unity.xr.core-utils` | 2.6.0 | transitive | registry |
| `com.unity.xr.legacyinputhelpers` | 2.1.13 | transitive | registry |
| `com.unity.mathematics` | 1.3.3 | transitive | registry |
| `com.unity.ugui` | 2.0.0 | transitive | **builtin** (bundled with the Editor) |

**Observation that corrects an earlier assumption:** the lock records the packages' declared dependency minimums (e.g. OpenXR → `core-utils@2.3.0`, `legacyinputhelpers@2.1.2`; XRI → `core-utils@2.4.0`, `mathematics@1.2.6`), but Unity resolved the shared transitive packages to the **newest registry versions** (`core-utils` 2.6.0, `legacyinputhelpers` 2.1.13, `mathematics` 1.3.3). The G2 pre-flight note that these would pin to minimums was wrong. The four direct versions are exactly as approved.

### 6.4 Unity run (batch)
- Command: `Unity.exe -batchmode -nographics -projectPath D:\arvr\Group_05_Sakshi_Aryan -quit -logFile …\unity_g2_resolve.log`. **Exit code 0**, 39.1 s.
- Log: `Package Manager: Done resolving packages in 13.46 seconds`; `Lock file was modified`; `Registered 45 packages` (previously 37); `Tundra build success (14.85 seconds), 345 items updated`. **No `error CS`, no warnings.** The OpenXR subsystem manifest was discovered (1 'inputs', 1 'displays' descriptor). The only "Error" line is the same batch-mode licensing-token notice seen in earlier runs. One `Curl error 42: Callback aborted` appears **after** "Batchmode quit successfully invoked" (shutdown); cause not established, no visible effect.

### 6.5 Regression tests after the install
| Check | Result |
|---|---|
| External compile check (15 files, 3 configurations) | PASS, exit 0 |
| Standalone C# tests | PASS, 161 passed, 0 failed, exit 0 |
| Python analysis tests | PASS, 34 passed, exit 0 |
| Unity batch self-tests (`VoiceVRGazeSelfTests.RunBatch`) | **PASS, 26 passed, 0 failed, 0 blocked, 26/26 expected, exit 0** (8.1 s); results `telemetry/selftest/unity_gaze_selftest_20261017T151115Z.txt` |

### 6.6 What G2 does and does not establish
- **Established:** the four packages install and resolve on Unity 6000.3.23f1, the project compiles with them, and the existing gaze logic and tests are unaffected.
- **NOT established:** that XR works. OpenXR is **not enabled** (no XR Plug-in Management settings exist, G4), the Input System package is installed but the project still uses the legacy handler (so XR input actions are inert until G5), there is no XR rig in the scene (G6), and nothing was run against the simulator. Meta XR Simulator × Unity 6.3 compatibility remains **unconfirmed** until a Play Mode run in the simulator.

### 6.7 Remaining risks
1. The simulator may not connect if the OpenXR package is changed while the Editor is open (Meta known issue): keep the Editor closed during package changes.
2. When the Editor is next opened with the Input System package present and the handler at `0`, Unity may show a dialog offering to enable the new input backends. Answer **No** unless G5 is approved.
3. Transitive packages are at the registry's newest versions rather than the declared minimums; if a future problem appears, this is a candidate cause.
4. `packages-lock.json` and `manifest.json` are untracked (Git cannot restore them): the backup in `.backups/g2_20261017-203854\` is the only restore point.
5. Rollback (needs approval before deleting anything): restore the two `Packages` files from the backup with the Editor closed. `Library/PackageCache` is regenerable and is **not** to be deleted without asking.

### 6.8 Next approvals (none started)
**G4** (enable XR Plug-in Management + OpenXR for Windows Standalone, interaction profile) and **G5** (Active Input Handler → Both) are the next prerequisites for any XR input; **G6/G7** (scene rig, grabbables) follow.

---

## 7. G4 / G5 result (2026-10-17 20:51–20:57)

**G5 — EXECUTED. G4 — NOT APPLIED (needs the Editor UI).** Nothing else was changed: no package, scene, script, Build Settings scene list, professor file or Python code was touched. G3 and G6–G12 were not started.

### 7.1 G5 — Active Input Handler
- **Backup first:** `D:\arvr\Group_05_Sakshi_Aryan\.backups\g4g5_20261017-205343\` — 24 files (`Packages\manifest.json`, `Packages\packages-lock.json`, all 22 `ProjectSettings\*`), **0 SHA-256 mismatches**, fresh folder, nothing overwritten.
- **Change (Unity closed, file level):** `ProjectSettings/ProjectSettings.asset` line 778 `activeInputHandler: 0` → `activeInputHandler: 2` ("Both"). Line-level diff vs backup = exactly that one line; file size unchanged (795 lines before and after); the other 21 `ProjectSettings` files unchanged.
- **Verified value:** `2` before and after a Unity batch run (Unity did not rewrite or revert it). After that run the only difference from the backup anywhere in `ProjectSettings`/`Packages` is this line.
- **Unity accepted it:** batch run `Unity.exe -batchmode -nographics -projectPath … -executeMethod Group05.VoiceVR.EditorTools.VoiceVRGazeSelfTests.RunBatch -quit`, **exit 0**, 22.9 s. Log: `Input System module state changed to: Initialized`, `Registered 45 packages`, `Tundra build success (14.28 seconds), 188 items updated` (full recompile under "Both"), **no `error CS`**. Self-tests: **26 passed, 0 failed, 0 blocked, 26/26 expected**.
- Other regressions: external compile check PASS (15 files, 3 configurations, exit 0); standalone C# tests PASS (161/161, exit 0).
- **Consequence:** the legacy `Input` API used by the keyboard/mouse fallback (`DesktopVoiceInput`, `DesktopLookController`) and the new Input System now coexist. This is a **compile and edit-mode** result only. Keyboard behaviour in Play Mode under "Both" has not been observed.

### 7.2 Why G4 was not applied programmatically
XR Plug-in Management 4.6.1 creates its settings container through `XRGeneralSettingsPerBuildTarget.GetOrCreate()`, which is `internal` (verified in `Library/PackageCache/com.unity.xr.management@…/Editor/XRGeneralSettingsPerBuildTarget.cs`), and `OpenXRPackageSettings` is a non-public class. There is no public API to create the container; the public methods only operate on an existing one. Calling the internals by reflection from a batch script would work against unsupported surface and could leave inconsistent linked assets. The supported route is the Project Settings UI, which needs the Editor, so per the instructions this stops here.

**Important side effect of G4 (verified from package source):** the packages register their settings via `EditorBuildSettings.AddConfigObject(...)`, so enabling OpenXR will write `m_configObjects` entries into `ProjectSettings/EditorBuildSettings.asset` (currently `m_configObjects: {}`) and create assets under `Assets/XR/`. The scene list (`m_Scenes: []`) is not affected and stays empty.

### 7.3 G4 — manual steps (Unity Editor, then close it)
Pre-filled evidence for the profile choice is in §7.4.
1. Open the project in Unity Hub with **6000.3.23f1**. (No input-backend dialog is expected: the handler is already `Both`.)
2. **Edit › Project Settings › XR Plug-in Management.** Select the **Windows, Mac, Linux** tab (desktop icon). Tick **OpenXR** only. Do not tick any other provider and do not touch the Android tab. Leave **Initialize XR on Startup** at its default (checked).
3. In the left tree click **XR Plug-in Management › OpenXR** (Windows tab). Under **Interaction Profiles** click **+** and add **Oculus Touch Controller Profile** only. Do not add any other profile and do not enable any feature group or other feature.
4. If a validation warning icon appears, **do not press "Fix All"**; just tell me what it says.
5. **File › Save Project**, then **File › Exit** (Don't Save if it asks about the scene; the scene is not modified). **Do not press Play.**
6. Tell me "G4 UI done, Unity closed." I will then verify the generated files, `EditorBuildSettings.asset`, `activeInputHandler`, run a batch compile and the 26 self-tests, and record the result.

### 7.4 Interaction-profile choice (evidence; the simulator's own profile list is NOT documented)
| Fact | Source |
|---|---|
| The installed OpenXR 1.16.1 provides: Oculus Touch Controller (`com.unity.openxr.feature.input.oculustouch`, `/interaction_profiles/oculus/touch_controller`, **no OpenXR extension needed**), Meta Quest Touch Plus (`…metaquestplus`, needs `XR_META_touch_controller_plus`), Meta Quest Touch Pro (`…metaquestpro`, needs `XR_FB_touch_controller_pro`), Eye Gaze (`XR_EXT_eye_gaze_interaction`), HTC Vive, Khronos Simple, HP Reverb G2, Microsoft Motion, Valve Index, hand profiles | package source on disk |
| Meta: extension support "varies by device profile" in the simulator; default device Meta Quest 3; input sources include "Meta Quest controllers" | simulator getting-started / overview pages |
| Meta's "Use Interaction SDK with Unity XR" names the Oculus Touch Controller Profile to enable (and Eye Gaze only when gaze input is needed); Unity's Meta Quest setup docs recommend Oculus Touch / Touch Plus / Touch Pro | developers.meta.com, docs.unity3d.com |
| Neither the simulator input page nor the overview names the interaction profiles it exposes | `xrsim-input-keyboard`, `xrsim-intro` |

**Choice: Oculus Touch Controller Profile only** — it is the one profile that exists in the installed package, needs no extension string (so it cannot fail on an extension the selected simulator device doesn't advertise), and is the one Meta's Unity guidance names. Touch Plus/Pro and Eye Gaze are deliberately **not** enabled.
**This is an inference, not a documented simulator guarantee.** If Play Mode in the simulator later shows no controller binding, Touch Plus is the first candidate to add; that would be a separate approval.

### 7.5 Unresolved risks
1. **Compatibility is still unproven:** compile success ≠ simulator runtime success. No XR session has been started.
2. **Initialize XR on Startup (default on):** once OpenXR is enabled, entering Play Mode will try to start an OpenXR session using the machine's active runtime, which is the Meta XR Simulator. If it connects, `XRSettings.isDeviceActive` becomes true and `DesktopLookController` stands down (no mouse look) — the camera also has no tracked-pose driver until G6. To keep the desktop path in Play Mode, either untick **Initialize XR on Startup** (a setting change that needs your say) or avoid pressing Play until G6/G7. Not observed yet.
3. The simulator toggle/Unity activation path (Meta XR Core SDK menu) is not installed; the runtime is machine-wide (G1).
4. `EditorBuildSettings.asset` will contain XR config objects after G4; it is untracked, so the G4/G5 backup is the restore point (the generated `Assets/XR/` is not in it because it does not exist yet).
5. Rollback for G5 alone: restore `ProjectSettings\ProjectSettings.asset` from the G4/G5 backup with Unity closed.
