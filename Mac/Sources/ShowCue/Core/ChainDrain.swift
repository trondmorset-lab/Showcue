import Foundation

/// Kjører en kjede av oppgaver som hver kan legge til neste, uten rekursjon: den ytterste kallen tar alle i en løkke.
/// Brukes til auto-fortsett/auto-følg, så tusenvis av kjedede cuer ikke bruker opp stakken.
final class ChainDrain<T> {
    private var pending: [T] = []
    private var draining = false

    /// Legger oppgaven i køen. Er ingen i gang, kjøres køen til den er tom (også oppgaver som legges til underveis).
    func push(_ item: T, run: (T) -> Void) {
        pending.append(item)
        if draining { return }
        draining = true
        defer { draining = false; pending.removeAll() }
        while !pending.isEmpty { run(pending.removeFirst()) }
    }
}
