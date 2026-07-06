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

    // ---- stroke batching: many Set() calls, one undo step -------------------

    private VoxelModel? _batch;

    /// Snapshot the model; mutate it freely until CommitBatch. Nested batches
    /// are a bug, not a feature.
    public void BeginBatch()
    {
        if (_batch is not null) throw new InvalidOperationException("batch already open");
        _batch = Model.Clone();
    }

    /// Push the pre-batch snapshot as one undo step (dropped if nothing
    /// actually changed).
    public void CommitBatch()
    {
        if (_batch is null) return;
        if (!_batch.Voxels.AsSpan().SequenceEqual(Model.Voxels))
        {
            Push(_batch);
            Version++;
        }
        _batch = null;
    }

    /// Roll the model back to the pre-batch snapshot (e.g. a cancelled drag).
    public void AbandonBatch()
    {
        if (_batch is null) return;
        Model.RestoreVoxels(_batch.Voxels);
        _batch = null;
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
