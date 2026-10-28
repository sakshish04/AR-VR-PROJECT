# Questionnaire data provenance

- Participants P01-P10 completed the SUS and NASA-TLX on paper after their sessions (26-27 Oct 2026, NMIMS MPSTME AR/VR Lab).
- The team transcribed the paper answers into the Google Form response sheet on 28 Oct 2026. The sheet's Timestamp column was typed during transcription, not set by Google at submission, so it was dropped; the session date column is kept.
- The signed paper forms and consent forms are held by the team outside this repository.
- NASA-TLX was answered on a 0-10 scale; `tlx_responses.csv` stores each answer x10 (0-100). Raw TLX = mean of the six subscales.

Files:
- `form_responses_original.csv`: the Google Form response sheet exactly as downloaded (Timestamp column was typed during transcription).
- `questionnaire_responses.csv`: the same file minus the Timestamp column.
- `sus_responses.csv`, `tlx_responses.csv`: the same answers in the analysis format.
- `study_summary.csv`: output of `analyze_study.py`.
- `figures/`: output of `plot_study.py`.

Design as run: 9 voice + gaze sessions and 3 controller sessions; P03 and P10 did both conditions. Not fully within-subjects.
