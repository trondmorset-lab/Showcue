namespace ShowCue.Models;

/// <summary>Kjører en kjede av oppgaver som hver kan legge til neste, uten rekursjon: den ytterste kallen tar alle i en løkke.
/// Brukes til auto-fortsett/auto-følg, så tusenvis av kjedede cuer ikke bruker opp stakken.</summary>
public sealed class ChainDrain<T>
{
    private readonly Queue<T> _pending = new();
    private bool _draining;

    /// <summary>Legger oppgaven i køen. Er ingen i gang, kjøres køen til den er tom (også oppgaver som legges til underveis).
    /// Kaster <paramref name="run"/>, tømmes køen og unntaket går videre.</summary>
    public void Push(T item, Action<T> run)
    {
        _pending.Enqueue(item);
        if (_draining) return;
        _draining = true;
        try
        {
            while (_pending.Count > 0) run(_pending.Dequeue());
        }
        finally { _draining = false; _pending.Clear(); }
    }
}
