namespace ScreenshotTool.Editor;

public sealed class UndoStack
{
    private readonly Stack<IAnnotationCommand> _undoStack = new();
    private readonly Stack<IAnnotationCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public void Execute(IAnnotationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Do();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        var command = _redoStack.Pop();
        command.Do();
        _undoStack.Push(command);
        return true;
    }
}
