# Task 11 Report

## Important finding fix: apply persisted settings in editor and save dialog

**Issue:** Settings persisted via `AppSettings` but were unused after save — editor always opened with hardcoded color/thickness defaults and save dialog ignored `DefaultSaveDirectory`.

**Fix:** `EditorWindow` accepts optional `AppSettings`; applies `StrokeColor`/`StrokeThickness` to combo selection (safe hex parse with fallback) and sets `SaveFileDialog.InitialDirectory` when the configured directory exists. `Overlay_CaptureConfirmed` passes `_settings`.

**Commit:** `fix: apply settings defaults in editor and save dialog`

**Tests:** 20/20 passed (`~/.dotnet/dotnet test`).
