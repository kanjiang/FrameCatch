using ScreenshotTool.Editor;

namespace ScreenshotTool.Tests;

public class UndoStackTests
{
    [Fact]
    public void Execute_Undo_Redo_Works()
    {
        var list = new List<int>();
        var stack = new UndoStack();
        stack.Execute(new DelegateCommand(
            () => list.Add(1),
            () => list.RemoveAt(list.Count - 1)));
        Assert.Single(list);
        stack.Undo();
        Assert.Empty(list);
        stack.Redo();
        Assert.Single(list);
    }

    [Fact]
    public void AddAnnotationCommand_AddsAndRemoves()
    {
        var items = new List<AnnotationItem>();
        var annotation = new RectAnnotation(
            Guid.NewGuid(),
            new System.Windows.Rect(0, 0, 10, 10),
            System.Windows.Media.Colors.Red,
            2);
        var stack = new UndoStack();

        stack.Execute(new AddAnnotationCommand(items, annotation));
        Assert.Single(items);

        stack.Undo();
        Assert.Empty(items);

        stack.Redo();
        Assert.Single(items);
        Assert.Same(annotation, items[0]);
    }

    [Fact]
    public void MoveAnnotationCommand_TranslatesAndRestores()
    {
        var id = Guid.NewGuid();
        var original = new RectAnnotation(
            id,
            new System.Windows.Rect(0, 0, 10, 10),
            System.Windows.Media.Colors.Red,
            2);
        var items = new List<AnnotationItem> { original };
        var stack = new UndoStack();
        var delta = new System.Windows.Vector(5, 3);

        stack.Execute(new MoveAnnotationCommand(items, 0, delta));

        var moved = Assert.IsType<RectAnnotation>(items[0]);
        Assert.Equal(new System.Windows.Rect(5, 3, 10, 10), moved.Bounds);

        stack.Undo();
        var restored = Assert.IsType<RectAnnotation>(items[0]);
        Assert.Equal(original.Bounds, restored.Bounds);
    }

    private sealed class DelegateCommand : IAnnotationCommand
    {
        private readonly Action _do;
        private readonly Action _undo;

        public DelegateCommand(Action doAction, Action undoAction)
        {
            _do = doAction;
            _undo = undoAction;
        }

        public void Do() => _do();

        public void Undo() => _undo();
    }
}
