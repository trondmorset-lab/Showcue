import XCTest
@testable import ShowCue

/// Forhåndssjekk før show. Sjekklogikken testes med en falsk maskin (filer, lydenheter, skjermer). Samme tilfeller som i Windows-versjonen.
final class PreflightTests: XCTestCase {
    private final class FakeEnv: PreflightEnvironment {
        var files: [String: PreflightFileStatus] = [:]
        var audioOutputs: Set<String> = []
        var inputs: Set<String> = []
        var screens = 1
        func fileStatus(_ path: String) -> PreflightFileStatus { files[path] ?? .missing }
        func audioOutputExists(uid: String) -> Bool { audioOutputs.contains(uid) }
        func inputDeviceExists(uid: String) -> Bool { inputs.contains(uid) }
        func screenExists(_ index: Int) -> Bool { index >= 0 && index < screens }
    }

    private func run(_ cues: [Cue], env: FakeEnv = FakeEnv(), settings: AppSettings = AppSettings()) -> [PreflightFinding] {
        PreflightCheck.run(cues: cues, settings: settings, environment: env)
    }

    private func codes(_ f: [PreflightFinding], _ s: PreflightSeverity) -> [String] { f.filter { $0.severity == s }.map { $0.code } }

    private func cue(_ type: CueType, _ number: String, _ set: (Cue) -> Void = { _ in }) -> Cue {
        let c = Cue(type: type)
        c.number = number
        set(c)
        return c
    }

    func testEmptyShowOnlyGivesOkLines() {
        let f = run([])
        XCTAssertEqual(f.map { $0.code }, ["mediaOk", "valuesOk", "devicesOk", "numbersOk"])
        XCTAssertTrue(f.allSatisfy { $0.severity == .ok })
    }

    func testMediaFilesMustExistAndBeReadable() {
        let env = FakeEnv()
        env.files["a.wav"] = .ok
        env.files["b.wav"] = .unreadable
        let cues = [
            cue(.audio, "1") { $0.filePath = "a.wav" },
            cue(.audio, "2") { $0.filePath = "b.wav" },
            cue(.audio, "3") { $0.filePath = "borte.wav" },
            cue(.image, "4"),
            cue(.playlist, "5") { $0.files = ["a.wav", "borte2.wav"] },
            cue(.slideshow, "6"),
            cue(.web, "7") { $0.url = "https://example.org" },
        ]
        let f = run(cues, env: env)
        let errors = f.filter { $0.severity == .error }
        XCTAssertEqual(Array(errors.map { $0.code }.prefix(4)), ["mediaUnreadable", "mediaMissing", "mediaEmpty", "mediaMissing"])
        XCTAssertEqual(errors.map { $0.cueIndex }, [1, 2, 3, 4])
        XCTAssertEqual(codes(f, .warning), ["listEmpty"])
        XCTAssertFalse(f.contains { $0.code == "mediaOk" })
    }

    func testAllMediaOkIsCountedOncePerFile() {
        let env = FakeEnv()
        env.files["a.wav"] = .ok
        env.files["b.png"] = .ok
        let f = run([
            cue(.audio, "1") { $0.filePath = "a.wav" },
            cue(.image, "2") { $0.filePath = "b.png" },
            cue(.playlist, "3") { $0.files = ["a.wav", "A.WAV"] },
        ], env: env)
        XCTAssertEqual(f.first { $0.code == "mediaOk" }?.severity, .ok)
        XCTAssertTrue(codes(f, .error).isEmpty)
    }

    func testDisabledCuesAreSkippedButTheirNumbersCount() {
        let f = run([
            cue(.audio, "1") { $0.filePath = "borte.wav"; $0.disabled = true },
            cue(.fade, "1") { $0.target = "99"; $0.disabled = true },
        ])
        XCTAssertEqual(codes(f, .error), ["numberDuplicate"])
    }

    func testTargetsMustExist() {
        let f = run([
            cue(.memo, "1") { $0.text = "Intro" },
            cue(.fade, "2") { $0.target = "" },
            cue(.fade, "3") { $0.target = "99" },
            cue(.goto, "4") { $0.target = " a1 " },
            cue(.stop, "5") { $0.target = "" },        // stopp alt
            cue(.stop, "6") { $0.target = "1" },
            cue(.goto, "7") { $0.target = "7" },       // peker på seg selv
            cue(.memo, "A1") { $0.text = "x"; $0.disabled = true },
            cue(.goto, "8") { $0.target = "a1" },      // mål er deaktivert
        ])
        XCTAssertEqual(codes(f, .error), ["targetEmpty", "targetMissing"])
        XCTAssertEqual(codes(f, .warning), ["targetDisabled", "targetSelf", "targetDisabled"])
    }

    func testRequiredFieldsAndAddresses() {
        let f = run([
            cue(.osc, "1") { $0.host = ""; $0.address = "/x" },
            cue(.osc, "2") { $0.host = "10.0.0.1"; $0.address = "" },
            cue(.network, "3") { $0.networkProtocol = .udp; $0.host = " " },
            cue(.network, "4") { $0.networkProtocol = .http; $0.url = "http://" },
            cue(.network, "5") { $0.networkProtocol = .http; $0.url = "https://x.no/a" },
            cue(.light, "6") { $0.host = "" },
            cue(.midi, "7") { $0.messageType = .mscGo; $0.mscCueNumber = "" },
            cue(.web, "8") { $0.url = "https://" },
            cue(.web, "9") { $0.url = "www.x.no" },
            cue(.memo, "10") { $0.text = "  " },
        ])
        XCTAssertEqual(codes(f, .error), ["requiredEmpty", "requiredEmpty", "requiredEmpty", "urlInvalid", "requiredEmpty", "requiredEmpty", "urlInvalid"])
        XCTAssertEqual(codes(f, .warning), ["memoEmpty"])
    }

    func testWebAddressRules() {
        XCTAssertTrue(PreflightCheck.isWebAddress("https://x.no"))
        XCTAssertTrue(PreflightCheck.isWebAddress("www.x.no"))
        XCTAssertTrue(PreflightCheck.isWebAddress("file:///tmp/a.html"))
        XCTAssertFalse(PreflightCheck.isWebAddress(""))
        XCTAssertFalse(PreflightCheck.isWebAddress("https://"))
        XCTAssertFalse(PreflightCheck.isWebAddress("ftp://x.no"))
        XCTAssertTrue(PreflightCheck.isHttpUrl("HTTP://x"))
        XCTAssertFalse(PreflightCheck.isHttpUrl("http://a b"))
    }

    func testStartAndEndMustMakeSense() {
        let env = FakeEnv()
        env.files["a.wav"] = .ok
        let f = run([
            cue(.audio, "1") { $0.filePath = "a.wav"; $0.startTime = 10; $0.endTime = 5 },
            cue(.audio, "2") { $0.filePath = "a.wav"; $0.startTime = 30; $0.fileDuration = 20 },
            cue(.audio, "3") { $0.filePath = "a.wav"; $0.startTime = 5; $0.endTime = 8; $0.fileDuration = 20 },
        ], env: env)
        XCTAssertEqual(codes(f, .error), ["timeOrder", "startBeyondFile"])
    }

    /// Mac-modellen lagrer tall som de er (Windows-modellen gjør ugyldige tall om til gyldige), så her må sjekken fange dem.
    func testNonFiniteAndNegativeNumbersAreErrors() {
        let f = run([
            cue(.fade, "1") { $0.target = "2"; $0.fadeTime = .nan; $0.preWait = -3 },
            cue(.memo, "2") { $0.text = "x"; $0.preWait = .infinity },
        ])
        XCTAssertEqual(codes(f, .error), ["valueInvalid", "valueInvalid", "valueInvalid"])
        XCTAssertEqual(f.filter { $0.code == "valueInvalid" }.map { $0.cueIndex }, [0, 0, 1])
    }

    func testAudioOutputMustExistWhenShowUsesAudio() {
        let env = FakeEnv()
        let cues = [cue(.audio, "1")]
        // Standardenheten (tom UID) finnes alltid.
        XCTAssertFalse(run(cues, env: env).contains { $0.code == "audioDeviceMissing" })
        var s = AppSettings()
        s.audioDeviceUID = "enhet-1"
        XCTAssertTrue(run(cues, env: env, settings: s).contains { $0.code == "audioDeviceMissing" && $0.severity == .error })
        env.audioOutputs.insert("enhet-1")
        XCTAssertFalse(run(cues, env: env, settings: s).contains { $0.code == "audioDeviceMissing" })
        // Uten lydcuer spiller utgangen ingen rolle.
        s.audioDeviceUID = "borte"
        XCTAssertFalse(run([cue(.memo, "1") { $0.text = "x" }], env: FakeEnv(), settings: s).contains { $0.code == "audioDeviceMissing" })
    }

    func testPreviewAndInputDevices() {
        let env = FakeEnv()
        var s = AppSettings()
        s.previewDeviceUID = "hode"
        s.inputDeviceUID = "mik"
        let f = run([cue(.input, "1")], env: env, settings: s)
        XCTAssertEqual(codes(f, .error), ["inputDeviceMissing"])
        XCTAssertEqual(codes(f, .warning), ["previewDeviceMissing"])
        env.inputs.insert("mik")
        env.audioOutputs.insert("hode")
        XCTAssertTrue(run([cue(.input, "1")], env: env, settings: s).allSatisfy { $0.severity == .ok })
        // Ingen live-inngang i showet: inngangsenheten sjekkes ikke.
        var t = AppSettings()
        t.inputDeviceUID = "mik"
        XCTAssertTrue(codes(run([], env: FakeEnv(), settings: t), .error).isEmpty)
    }

    func testScreensMustExist() {
        let env = FakeEnv()
        env.screens = 2
        var s = AppSettings()
        s.videoScreen = 2
        let f = run([cue(.image, "1")], env: env, settings: s)
        XCTAssertTrue(f.contains { $0.code == "screenMissing" && $0.cueIndex == -1 && $0.severity == .warning })
        // Egen skjerm på cuen.
        let f2 = run([cue(.image, "2") { $0.outputScreen = 3 }], env: env, settings: s)
        XCTAssertEqual(f2.filter { $0.code == "screenMissing" }.map { $0.cueIndex }, [0])
        // Lysbildeserier og eksisterende skjerm.
        env.files["x.png"] = .ok
        let show = cue(.slideshow, "3") { $0.outputScreen = 1; $0.files = ["x.png"] }
        var off = AppSettings()
        off.videoScreen = -1
        XCTAssertFalse(run([show], env: env, settings: off).contains { $0.code == "screenMissing" })
        show.outputScreen = 5
        XCTAssertTrue(run([show], env: env, settings: off).contains { $0.code == "screenMissing" })
    }

    func testDuplicateNumbersIgnoreCaseAndSpacesButNotEmptyNumbers() {
        let f = run([
            cue(.memo, "1") { $0.text = "a" },
            cue(.memo, " 1 ") { $0.text = "b" },
            cue(.memo, "A") { $0.text = "c" },
            cue(.memo, "a") { $0.text = "d" },
            cue(.memo, "") { $0.text = "e" },
            cue(.memo, "") { $0.text = "f" },
            cue(.memo, "2") { $0.text = "g" },
        ])
        let dup = f.filter { $0.code == "numberDuplicate" }
        XCTAssertEqual(dup.count, 2)
        XCTAssertEqual(dup.map { $0.cueIndex }, [0, 2])
        XCTAssertFalse(f.contains { $0.code == "numbersOk" })
    }

    func testSystemEnvironmentSeesFilesAndDirectories() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("showcue-preflight-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let file = dir.appendingPathComponent("a.wav")
        try "x".write(to: file, atomically: true, encoding: .utf8)
        let env = SystemPreflightEnvironment()
        XCTAssertEqual(env.fileStatus(file.path), .ok)
        XCTAssertEqual(env.fileStatus(dir.appendingPathComponent("borte.wav").path), .missing)
        XCTAssertEqual(env.fileStatus(dir.path), .missing)
    }
}
