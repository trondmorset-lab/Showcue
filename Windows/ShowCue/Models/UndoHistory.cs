namespace ShowCue.Models;

/// <summary>
/// Angre/gjør om for showet. Hver tilstand er hele showet som JSON (showfilene er små),
/// så det blir enkelt og alltid riktig: angre = gå tilbake til forrige lagrede tilstand.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly int _limit;

    public UndoHistory(int limit = 200) { _limit = limit; }

    /// <summary>Tilstanden showet har nå.</summary>
    public string? Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Start på nytt (nytt eller åpnet show).</summary>
    public void Reset(string state)
    {
        _undo.Clear();
        _redo.Clear();
        Current = state;
    }

    /// <summary>Husk en ny tilstand. Returnerer false hvis ingenting er endret.</summary>
    public bool Record(string state)
    {
        if (state == Current) return false;
        if (Current != null)
        {
            _undo.Add(Current);
            if (_undo.Count > _limit) _undo.RemoveAt(0);
        }
        _redo.Clear();
        Current = state;
        return true;
    }

    /// <summary>Gå ett steg tilbake. Returnerer tilstanden som skal vises, eller null.</summary>
    public string? Undo()
    {
        if (!CanUndo) return null;
        _redo.Push(Current!);
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return Current;
    }

    /// <summary>Gjør om det som ble angret. Returnerer tilstanden som skal vises, eller null.</summary>
    public string? Redo()
    {
        if (!CanRedo) return null;
        _undo.Add(Current!);
        Current = _redo.Pop();
        return Current;
    }
}
