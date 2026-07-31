# Task 9 Report

## Important finding fix: TextBox owns undo/delete while editing

**Issue:** `EditorWindow.PreviewKeyDown` intercepted Ctrl+Z/Y globally. While inline `TextBox` was editing, Ctrl+Z committed text and undid canvas history instead of undoing typed characters.

**Fix:** In `Window_PreviewKeyDown`, return early when `Keyboard.FocusedElement is TextBox` so Ctrl+Z/Y and Delete are not marked handled and do not call `AnnotationCanvas.Undo`/`Redo`/`DeleteSelection`.

**Commit:** `fix: let TextBox own undo shortcuts while editing`
