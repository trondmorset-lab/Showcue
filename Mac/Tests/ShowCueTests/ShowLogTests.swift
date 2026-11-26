import XCTest
@testable import ShowCue

/// Showloggen: formatering, escaping, CSV, filrotasjon, begrenset kø og skriving til fil.
final class ShowLogTests: XCTestCase {
    private let t0 = ShowLogTime(unixMs: 1_795_611_802_123, offsetMinutes: 60) // 2026-11-25T14:03:22.123+01:00

    private func ev(_ kind: ShowLogKind = .start, text: String = "", name: String = "Intro", run: Int64 = 83456) -> ShowLogEvent {
        ShowLogEvent(time: t0, runMs: run, kind: kind, number: "3", name: name, cueType: "audio", how: "go", text: text)
    }

    private func tempDir() -> URL {
        let d = FileManager.default.temporaryDirectory.appendingPathComponent("showcue-log-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
        return d
    }

    func testTruncateKeepsShortTextAndCutsLongText() {
        XCTAssertEqual(ShowLogFormat.truncate(nil, 5), "")
        XCTAssertEqual(ShowLogFormat.truncate("abc", 5), "abc")
        XCTAssertEqual(ShowLogFormat.truncate("abcde", 5), "abcde")
        let cut = ShowLogFormat.truncate("abcdefghij", 5)
        XCTAssertEqual(cut, "abcd…")
        XCTAssertEqual(cut.count, 5)
        XCTAssertEqual(ShowLogFormat.truncate(String(repeating: "x", count: 5000), 80).count, 80)
    }

    func testEscapeRemovesControlCharactersAndRoundTrips() {
        let raw = "a\tb\nc\rd\\e\u{01}\u{7F}\u{2028}é"
        let esc = ShowLogFormat.escape(raw)
        XCTAssertEqual(esc, "a\\tb\\nc\\rd\\\\e\\x01\\x7F\\u2028é")
        XCTAssertFalse(esc.unicodeScalars.contains { $0.value < 0x20 })
        XCTAssertEqual(ShowLogFormat.unescape(esc), raw)
        XCTAssertEqual(ShowLogFormat.unescape("\\q"), "\\q", "ukjent tegnfølge beholdes")
        XCTAssertEqual(ShowLogFormat.unescape("x\\"), "x\\")
    }

    func testRunTextFormatsAndParses() {
        XCTAssertEqual(ShowLogFormat.runText(0), "00:00:00.000")
        XCTAssertEqual(ShowLogFormat.runText(-5), "00:00:00.000")
        XCTAssertEqual(ShowLogFormat.runText(83456), "00:01:23.456")
        XCTAssertEqual(ShowLogFormat.runText(360_000_000), "100:00:00.000")
        XCTAssertEqual(ShowLogFormat.parseRun("100:00:00.000"), 360_000_000)
        XCTAssertNil(ShowLogFormat.parseRun("00:61:00.000"))
        XCTAssertNil(ShowLogFormat.parseRun("tull"))
        XCTAssertNil(ShowLogFormat.parseRun("00:00:00.5"))
    }

    func testFormatLineGivesExactTextLine() {
        XCTAssertEqual(ShowLogFormat.formatLine(ev()), "2026-11-25T14:03:22.123+01:00\t00:01:23.456\tstart\t3\taudio\tgo\tIntro\t")
        let line = ShowLogFormat.formatLine(ev(.error, text: "linje1\nlinje2\ttab", name: "Navn\twith tab"))
        XCTAssertFalse(line.contains("\n"))
        XCTAssertEqual(line.components(separatedBy: "\t").count, 8)
        let long = ShowLogFormat.formatLine(ev(text: String(repeating: "z", count: 1000), name: String(repeating: "n", count: 1000)))
        XCTAssertLessThan(long.count, 500)
    }

    func testParseLineRoundTripsAndRejectsGarbage() {
        let e = ev(.warning, text: "fil \\\\server\\x.wav mangler\nlinje 2", name: "Æøå 🔒")
        guard let back = ShowLogFormat.parseLine(ShowLogFormat.formatLine(e)) else { return XCTFail("kunne ikke leses") }
        XCTAssertEqual(back.kind, e.kind)
        XCTAssertEqual(back.text, e.text)
        XCTAssertEqual(back.name, e.name)
        XCTAssertEqual(back.runMs, e.runMs)
        XCTAssertEqual(back.time, e.time)
        XCTAssertEqual(back.time.offsetMinutes, 60)
        XCTAssertNil(ShowLogFormat.parseLine("# kommentar"))
        XCTAssertNil(ShowLogFormat.parseLine(""))
        XCTAssertNil(ShowLogFormat.parseLine("a\tb\tc"))
        XCTAssertNil(ShowLogFormat.parseLine("ikke-tid\t00:00:00.000\tstart\t1\taudio\tgo\tx\t"))
        XCTAssertNil(ShowLogFormat.parseLine("2026-11-25T14:03:22.123+01:00\t00:00:00.000\tukjent\t1\taudio\tgo\tx\t"))
    }

    func testIsoRoundTripsAroundEpochAndLeapDays() {
        for (ms, off) in [(Int64(0), 0), (Int64(1_709_209_200_000), -330), (Int64(951_782_400_000), 120), (Int64(-86_400_000), 0)] {
            let t = ShowLogTime(unixMs: ms, offsetMinutes: off)
            XCTAssertEqual(ShowLogFormat.parseIso(ShowLogFormat.iso(t)), t)
        }
        XCTAssertEqual(ShowLogFormat.iso(ShowLogTime(unixMs: 0, offsetMinutes: 0)), "1970-01-01T00:00:00.000+00:00")
        XCTAssertNil(ShowLogFormat.parseIso("2026-02-30T10:00:00.000+01:00"))
    }

    func testParseTextSkipsBadLinesAndCountsThem() {
        let text = ShowLogExport.toText([ev(), ev(.finish)]) + "skrap\n\n"
        let r = ShowLogExport.parse(text.components(separatedBy: "\n"))
        XCTAssertEqual(r.events.count, 2)
        XCTAssertEqual(r.skipped, 1)
    }

    func testCsvCellEscapesAndNeutralisesFormulas() {
        let cases: [(String, String)] = [
            ("abc", "abc"), ("", ""), ("=1+1", "'=1+1"), ("+1", "'+1"), ("-1", "'-1"), ("@x", "'@x"), ("\tx", "' x"),
            ("a,b", "\"a,b\""), ("say \"hi\"", "\"say \"\"hi\"\"\""), ("l1\nl2", "\"l1\nl2\""), (" lead", "\" lead\""), ("a\u{01}b", "a b"),
        ]
        for (input, expected) in cases { XCTAssertEqual(ShowLogExport.csvCell(input), expected, input) }
    }

    func testCsvHasHeaderAndOneRowPerEvent() {
        let csv = ShowLogExport.toCSV([ev(.error, text: "=cmd|' /C calc'!A0"), ev(.finish, text: "a,b")])
        let lines = csv.components(separatedBy: "\r\n")
        XCTAssertEqual(lines[0], "time,run,event,cue,type,how,name,text")
        XCTAssertEqual(lines[1], "2026-11-25T14:03:22.123+01:00,00:01:23.456,error,3,audio,go,Intro,'=cmd|' /C calc'!A0")
        XCTAssertEqual(lines[2], "2026-11-25T14:03:22.123+01:00,00:01:23.456,finish,3,audio,go,Intro,\"a,b\"")
        XCTAssertEqual(lines[3], "")
        XCTAssertEqual(lines.count, 4)
    }

    func testFileNamesAndRotation() {
        XCTAssertEqual(ShowLogFiles.fileName(start: t0), "showlog 2026-11-25 14.03.22.log")
        XCTAssertEqual(ShowLogFiles.fileName(start: t0, attempt: 2), "showlog 2026-11-25 14.03.22 (2).log")
        XCTAssertTrue(ShowLogFiles.isLogName("showlog 2026-11-25 14.03.22.log"))
        XCTAssertTrue(ShowLogFiles.isLogName("showlog 2026-11-25 14.03.22 (3).log"))
        XCTAssertFalse(ShowLogFiles.isLogName("notes.log"))
        XCTAssertFalse(ShowLogFiles.isLogName("showlog 2026-11-25 14.03.22.log.bak"))
        XCTAssertFalse(ShowLogFiles.isLogName(nil))

        let names = ["showlog 2026-11-20 10.00.00.log", "showlog 2026-11-22 10.00.00.log", "showlog 2026-11-21 10.00.00.log",
                     "showlog 2026-11-24 10.00.00.log", "showlog 2026-11-23 10.00.00.log", "readme.txt", "Show.showcue"]
        XCTAssertEqual(ShowLogFiles.toDelete(names, keep: 3), ["showlog 2026-11-21 10.00.00.log", "showlog 2026-11-20 10.00.00.log"])
        XCTAssertTrue(ShowLogFiles.toDelete(names, keep: 30).isEmpty)
        XCTAssertEqual(ShowLogFiles.toDelete(names, keep: 0).count, 5)
    }

    func testBoundedQueueDropsOldestAndCounts() {
        let q = BoundedQueue<Int>(capacity: 3)
        XCTAssertTrue(q.enqueue(1))
        XCTAssertTrue(q.enqueue(2))
        XCTAssertTrue(q.enqueue(3))
        XCTAssertFalse(q.enqueue(4))
        XCTAssertFalse(q.enqueue(5))
        XCTAssertEqual(q.count, 3)
        XCTAssertEqual(q.pendingDropped, 2)
        var got: [Int] = []
        XCTAssertEqual(q.drain(into: &got, max: 10), 3)
        XCTAssertEqual(got, [3, 4, 5])
        XCTAssertEqual(q.takeDropped(), 2)
        XCTAssertEqual(q.takeDropped(), 0)
        XCTAssertEqual(q.droppedTotal, 2)
        XCTAssertEqual(BoundedQueue<Int>(capacity: 0).capacity, 1)
        var part: [Int] = []
        q.enqueue(7); q.enqueue(8)
        XCTAssertEqual(q.drain(into: &part, max: 1), 1)
        XCTAssertEqual(q.count, 1)
    }

    func testRecordKeepsTimesAndSourceAndLimitsMemory() {
        var now = t0
        let log = ShowLog(root: nil, clock: { now }, memoryLimit: 3)
        defer { log.close() }
        now = t0.adding(ms: 1500)
        log.record(.start, number: "1", name: "A", cueType: "audio", how: .go)
        ShowLog.via(.osc) {
            log.record(.pause)
            ShowLog.via(.midi, keepExisting: true) { log.record(.resume) }
        }
        log.record(.stopAll)
        log.record(.error, text: String(repeating: "x", count: 1000))
        XCTAssertEqual(log.total, 5)
        let snap = log.snapshot()
        XCTAssertEqual(snap.count, 3)
        XCTAssertEqual(snap.map { ShowLogFormat.kindToken($0.kind) }, ["resume", "stopall", "error"])
        XCTAssertEqual(snap[0].how, "osc")
        XCTAssertEqual(snap[1].how, "")
        XCTAssertEqual(snap[0].runMs, 1500)
        XCTAssertEqual(snap[2].text.count, ShowLogFormat.maxText)
        let more = log.readSince(4)
        XCTAssertEqual(more.events.count, 1)
        XCTAssertEqual(more.last, 5)
    }

    func testWritesFileWithHeaderAndLines() throws {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let log = ShowLog(root: root, clock: { self.t0 })
        log.record(.appStart, text: "ShowCue test")
        log.record(.start, number: "1", name: "Intro\nny linje", cueType: "audio", how: .go)
        XCTAssertTrue(log.flush())
        XCTAssertEqual(log.filePath.map { ($0 as NSString).lastPathComponent }, "showlog 2026-11-25 14.03.22.log")
        log.close()
        let file = ShowLogFiles.directory(root: root).appendingPathComponent("showlog 2026-11-25 14.03.22.log")
        let lines = try String(contentsOf: file, encoding: .utf8).components(separatedBy: "\n").filter { !$0.isEmpty }
        XCTAssertTrue(lines[0].hasPrefix(ShowLogFormat.fileHeader))
        XCTAssertEqual(lines.count, 3)
        XCTAssertTrue(lines[1].contains("\tappstart\t"))
        XCTAssertTrue(lines[2].contains("\tstart\t1\taudio\tgo\tIntro\\nny linje\t"))
        let back = try ShowLogExport.readFile(file)
        XCTAssertEqual(back.events.count, 2)
        XCTAssertEqual(back.skipped, 0)
        XCTAssertEqual(back.events[1].name, "Intro\nny linje")
    }

    func testKeepsOnlyTheNewestFilesAndNeverTouchesOthers() throws {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let dir = ShowLogFiles.directory(root: root)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for d in 1...5 { try "x".write(to: dir.appendingPathComponent("showlog 2026-11-0\(d) 10.00.00.log"), atomically: true, encoding: .utf8) }
        try "x".write(to: dir.appendingPathComponent("notes.txt"), atomically: true, encoding: .utf8)
        let log = ShowLog(root: root, clock: { self.t0 }, filesToKeep: 3)
        log.record(.appStart)
        XCTAssertTrue(log.flush())
        log.close()
        let left = try FileManager.default.contentsOfDirectory(atPath: dir.path).sorted { $0.utf8.lexicographicallyPrecedes($1.utf8) }
        XCTAssertEqual(left, ["notes.txt", "showlog 2026-11-04 10.00.00.log", "showlog 2026-11-05 10.00.00.log", "showlog 2026-11-25 14.03.22.log"])
    }

    func testStopsWritingAtTheSizeLimit() throws {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let log = ShowLog(root: root, clock: { self.t0 }, maxFileBytes: 1024)
        for i in 0..<100 { log.record(.start, number: "\(i)", name: "Cue \(i)", cueType: "audio", how: .go) }
        XCTAssertTrue(log.flush())
        XCTAssertEqual(log.total, 100)
        let path = try XCTUnwrap(log.filePath)
        log.close()
        let size = (try FileManager.default.attributesOfItem(atPath: path))[.size] as? Int ?? 0
        XCTAssertLessThanOrEqual(size, 1024)
        XCTAssertTrue(try String(contentsOfFile: path, encoding: .utf8).contains("size limit reached"))
    }

    func testFileErrorIsSwallowedAndReportedOnce() throws {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let blocker = root.appendingPathComponent("data")
        try "this is a file, not a folder".write(to: blocker, atomically: true, encoding: .utf8)
        let lock = NSLock()
        var reports = 0
        let log = ShowLog(root: blocker, clock: { self.t0 })
        log.onFault = { _ in lock.lock(); reports += 1; lock.unlock() }
        for i in 0..<20 { log.record(.start, number: "\(i)") }
        log.flush()
        log.record(.stop, number: "1")
        log.flush()
        log.close()
        XCTAssertNotNil(log.fault)
        lock.lock(); XCTAssertEqual(reports, 1); lock.unlock()
        XCTAssertEqual(log.total, 21, "minnet virker selv om filen ikke gjør det")
    }

    func testNoFileIsCreatedWhenWritingIsOff() {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let log = ShowLog(root: root, writeFile: false, clock: { self.t0 })
        log.record(.appStart)
        XCTAssertTrue(log.flush())
        XCTAssertEqual(log.total, 1)
        log.close()
        XCTAssertFalse(FileManager.default.fileExists(atPath: ShowLogFiles.directory(root: root).path))
    }

    func testManyThreadsNeverThrowAndNothingIsLostInMemory() {
        let root = tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let log = ShowLog(root: root, queueCapacity: 16, memoryLimit: 100)
        DispatchQueue.concurrentPerform(iterations: 6) { t in
            for i in 0..<2000 { log.record(.start, number: "\(i)", name: "T\(t)", cueType: "audio", how: .go, text: "x\ny") }
        }
        XCTAssertTrue(log.flush(timeout: 5))
        XCTAssertGreaterThanOrEqual(log.total, 12000, "alle ble talt i minnet (pluss evt. «dropped»-linjer)")
        XCTAssertLessThanOrEqual(log.snapshot().count, 100)
        log.close()
    }

    func testStaticFacadeDoesNothingWithoutLogAndNeverThrows() {
        let before = ShowLog.current
        defer { ShowLog.current = before }
        ShowLog.current = nil
        ShowLog.add(.error, text: "x")
        ShowLog.add(.start, cue: nil)
        let mine = ShowLog(root: nil)
        ShowLog.current = mine
        let cue = Cue(type: .audio)
        cue.number = "7"; cue.name = "Fanfare"
        ShowLog.add(.start, cue: cue, how: .hotkey)
        XCTAssertEqual(mine.snapshot().count, 1)
        let e = mine.snapshot()[0]
        XCTAssertEqual(e.number, "7")
        XCTAssertEqual(e.name, "Fanfare")
        XCTAssertEqual(e.cueType, "audio")
        XCTAssertEqual(e.how, "hotkey")
        mine.close()
        mine.record(.error) // etter close: ingen feil
        XCTAssertEqual(mine.snapshot().count, 1)
    }

    func testCategoriesFilterTheEvents() {
        XCTAssertTrue(ShowLogFormat.matches(ev(.error), .problems))
        XCTAssertTrue(ShowLogFormat.matches(ev(.warning), .problems))
        XCTAssertFalse(ShowLogFormat.matches(ev(.start), .problems))
        XCTAssertTrue(ShowLogFormat.matches(ev(.start), .cues))
        XCTAssertTrue(ShowLogFormat.matches(ev(.stopAll), .control))
        XCTAssertTrue(ShowLogFormat.matches(ev(.showSaved), .show))
        XCTAssertTrue(ShowLogFormat.matches(ev(.appExit), .all))
    }

    func testSettingIsOnByDefaultAndSurvivesRoundTrip() throws {
        XCTAssertTrue(AppSettings().writeShowLog)
        XCTAssertTrue(try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8)).writeShowLog)
        var off = AppSettings(); off.writeShowLog = false
        XCTAssertFalse(try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(off)).writeShowLog)
    }
}
