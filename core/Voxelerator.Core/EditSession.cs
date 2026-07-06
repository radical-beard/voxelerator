namespace Voxelerator.Core;

/// One open model + its undo history. In-place edits go through Do(); ops
/// that change dimensions go through Swap(). External changes (MCP writing
/// the same folder) arrive via AbsorbExternal() so they land on the undo
/// stack like any other edit.
public sealed class EditSession
{
    private const int MaxUndo = 256;

    public VoxelModel Model { get; private set; }
    private readonly LinkedList<VoxelModel> _undo = new();
    private readonly Stack<VoxelModel> _redo = new();

    /// Bumped on every mutation — cheap dirty-tracking for autosave/UI.
    public long Version { get; private set; }

    public EditSession(VoxelModel model) { Model = model; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Do(Action<VoxelModel> edit)
    {
        Push(Model.Clone());
        edit(Model);
        Version++;
    }

    public void Swap(Func<VoxelModel, VoxelModel> produce)
    {
        var next = produce(Model);
        Push(Model);
        Model = next;
        Version++;
    }

    public void AbsorbExternal(VoxelModel reloaded)
    {
        Push(Model);
        Model = reloaded;
        Version++;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Model);
        Model = _undo.Last!.Value;
        _undo.RemoveLast();
        Version++;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.AddLast(Model);                        // do NOT clear redo here
        while (_undo.Count > MaxUndo) _undo.RemoveFirst();
        Model = _redo.Pop();
        Version++;
        return true;
    }

    private void Push(VoxelModel snapshot)
    {
        _redo.Clear();
        _undo.AddLast(snapshot);
        while (_undo.Count > MaxUndo) _undo.RemoveFirst();
    }
}
