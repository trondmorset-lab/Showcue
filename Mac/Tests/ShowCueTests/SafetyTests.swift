import XCTest
import AVFoundation
@testable import ShowCue

/// Angre, autolagring/sikkerhetskopier og egen lytteutgang.
final class SafetyTests: XCTestCase {
    private var root: URL!

    override func setUp() {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("showcue-safety-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    private func d(_ s: String) -> Data { Data(s.utf8) }

    func testUndoAndRedo() {
        let h = UndoHistory()
        h.reset(d("a"))
        XCTAssertFalse(h.canUndo)
        XCTAssertTrue(h.record(d("b")))
        XCTAssertTrue(h.record(d("c")))
        XCTAssertFalse(h.record(d("c")), "uendret = ingen nytt steg")
        XCTAssertEqual(h.undo(), d("b"))
        XCTAssertEqual(h.undo(), d("a"))
        XCTAssertNil(h.undo())
        XCTAssertEqual(h.redo(), d("b"))
        XCTAssertTrue(h.record(d("x")))
        XCTAssertFalse(h.canRedo, "ny endring etter angre fjerner «gjør om»")
        XCTAssertEqual(h.undo(), d("b"))
    }

    func testUndoLimit() {
        let h = UndoHistory(limit: 3)
        h.reset(d("0"))
        for i in 1...5 { h.record(d(String(i))) }
        XCTAssertEqual(h.undo(), d("4"))
        XCTAssertEqual(h.undo(), d("3"))
        XCTAssertEqual(h.undo(), d("2"))
        XCTAssertFalse(h.canUndo)
    }

    func testAutosaveAndRecovery() throws {
        let show = root.appendingPathComponent("Konsert.showcue")
        try d("{}").write(to: show)
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-300)], ofItemAtPath: show.path)
        XCTAssertNil(ShowSafety.pendingAutosave(for: show, root: root))

        try ShowSafety.writeAutosave(for: show, data: d("{\"Title\":\"x\"}"), root: root)
        let pending = try XCTUnwrap(ShowSafety.pendingAutosave(for: show, root: root))
        XCTAssertTrue(pending.lastPathComponent.hasPrefix("Konsert "))
        XCTAssertEqual(try Data(contentsOf: pending), d("{\"Title\":\"x\"}"))

        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(300)], ofItemAtPath: show.path)
        XCTAssertNil(ShowSafety.pendingAutosave(for: show, root: root), "lagret etterpå → ingen gjenoppretting")

        ShowSafety.deleteAutosave(for: show, root: root)
        XCTAssertFalse(FileManager.default.fileExists(atPath: ShowSafety.autosaveURL(for: show, root: root).path))

        try ShowSafety.writeAutosave(for: nil, data: d("{}"), root: root)
        XCTAssertNotNil(ShowSafety.pendingAutosave(for: nil, root: root))
        XCTAssertNotEqual(ShowSafety.autosaveURL(for: root.appendingPathComponent("a/Show.showcue"), root: root),
                          ShowSafety.autosaveURL(for: root.appendingPathComponent("b/Show.showcue"), root: root))
    }

    func testBackupsKeepTheNewest() throws {
        let show = root.appendingPathComponent("Revy.showcue")
        try d("v").write(to: show)
        let t0 = DateComponents(calendar: Calendar(identifier: .gregorian), year: 2026, month: 11, day: 1, hour: 12).date!
        for i in 0..<(ShowSafety.backupsToKeep + 5) { ShowSafety.backup(show, root: root, now: t0.addingTimeInterval(Double(i) * 60)) }
        let files = try FileManager.default.contentsOfDirectory(atPath: ShowSafety.backupDir(root: root).path).filter { $0.hasPrefix("Revy ") }
        XCTAssertEqual(files.count, ShowSafety.backupsToKeep)
        XCTAssertTrue(files.contains("Revy 2026-11-01 12.24.00.showcue"))
        XCTAssertFalse(files.contains("Revy 2026-11-01 12.00.00.showcue"))
        XCTAssertNil(ShowSafety.backup(root.appendingPathComponent("finnes-ikke.showcue"), root: root))
    }

    func testDownmix() {
        XCTAssertEqual(AudioRoute.downmix(fileChannels: 1, leftBus: 5), [AudioRoute(1, 5), AudioRoute(1, 6)])
        XCTAssertEqual(AudioRoute.downmix(fileChannels: 2, leftBus: 3), [AudioRoute(1, 3), AudioRoute(2, 4)])
        XCTAssertEqual(AudioRoute.downmix(fileChannels: 4, leftBus: 1),
                       [AudioRoute(1, 1, -3), AudioRoute(2, 2, -3), AudioRoute(3, 1, -3), AudioRoute(4, 2, -3)])
    }

    func testPreviewGoesToListenBusNotTheCueOutputs() throws {
        let rate = 48000.0
        let url = root.appendingPathComponent("stereo.wav")
        let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 2, interleaved: false)!
        let file = try AVAudioFile(forWriting: url, settings: fmt.settings, commonFormat: .pcmFormatFloat32, interleaved: false)
        let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: 48000)!
        buf.frameLength = 48000
        for i in 0..<48000 { buf.floatChannelData![0][i] = 0.1; buf.floatChannelData![1][i] = 0.2 }
        try file.write(from: buf)

        let m = PatchMixer(sampleRate: rate, deviceChannels: 8, patch: PatchBus.defaultPatch(8))
        let cue = Cue(type: .audio)
        cue.filePath = url.path
        cue.routes = [AudioRoute(1, 1), AudioRoute(2, 2)]

        XCTAssertTrue(AudioPreview(mixer: { m }).goesToShowOutputs)

        let toHeadphones = AudioPreview(target: { AudioPreview.Target(mixer: m, leftBus: 7) })
        XCTAssertFalse(toHeadphones.goesToShowOutputs)
        XCTAssertTrue(toHeadphones.play(cue, from: 0))
        toHeadphones.stream?.fill()
        let o = m.renderInterleaved(frames: 1000)
        XCTAssertEqual(o[900 * 8 + 0], 0, accuracy: 1e-6, "ikke til salen (utgang 1)")
        XCTAssertEqual(o[900 * 8 + 1], 0, accuracy: 1e-6, "ikke til salen (utgang 2)")
        XCTAssertEqual(o[900 * 8 + 6], 0.1, accuracy: 0.002, "venstre → utgang 7")
        XCTAssertEqual(o[900 * 8 + 7], 0.2, accuracy: 0.002, "høyre → utgang 8")
        toHeadphones.stop()

        XCTAssertFalse(AudioPreview(target: { nil }).play(cue, from: 0), "hodetelefonene mangler → ingen lyd (aldri til salen)")
    }
}
