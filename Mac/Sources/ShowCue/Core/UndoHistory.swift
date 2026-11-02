import Foundation

/// Angre/gjør om for showet. Hver tilstand er hele showet som JSON (showfilene er små),
/// så det blir enkelt og alltid riktig: angre = gå tilbake til forrige lagrede tilstand.
final class UndoHistory {
    private var undoStack: [Data] = []
    private var redoStack: [Data] = []
    private let limit: Int

    init(limit: Int = 200) { self.limit = limit }

    /// Tilstanden showet har nå.
    private(set) var current: Data?
    var canUndo: Bool { !undoStack.isEmpty }
    var canRedo: Bool { !redoStack.isEmpty }

    /// Start på nytt (nytt eller åpnet show).
    func reset(_ state: Data) {
        undoStack.removeAll()
        redoStack.removeAll()
        current = state
    }

    /// Husk en ny tilstand. Returnerer false hvis ingenting er endret.
    @discardableResult
    func record(_ state: Data) -> Bool {
        guard state != current else { return false }
        if let c = current {
            undoStack.append(c)
            if undoStack.count > limit { undoStack.removeFirst() }
        }
        redoStack.removeAll()
        current = state
        return true
    }

    /// Gå ett steg tilbake. Returnerer tilstanden som skal vises, eller nil.
    func undo() -> Data? {
        guard let prev = undoStack.popLast(), let c = current else { return nil }
        redoStack.append(c)
        current = prev
        return prev
    }

    /// Gjør om det som ble angret. Returnerer tilstanden som skal vises, eller nil.
    func redo() -> Data? {
        guard let next = redoStack.popLast(), let c = current else { return nil }
        undoStack.append(c)
        current = next
        return next
    }
}
