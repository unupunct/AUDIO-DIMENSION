import XCTest

final class PresetTests: XCTestCase {
    func library(_ folder: URL = TestPaths.temp(), _ d: UserDefaults = TestPaths.defaults()) -> PresetLibrary {
        PresetLibrary(resources: TestPaths.resources, customFolder: folder, defaults: d)
    }

    func testAll60BuiltInsParseAndValidate() {
        let all = TestPaths.allPresets()
        XCTAssertEqual(all.count, 60)
        XCTAssertEqual(Set(all.map { $0.id }).count, 60)
        XCTAssertEqual(Set(all.map { $0.category }).count, 7)
    }

    func testLibraryShowsOnlyImplementedPresets() {
        let lib = library()
        XCTAssertEqual(lib.builtIn.count + lib.unavailableCount, 60)
        XCTAssertEqual(lib.builtIn.count, 25)
        for p in lib.builtIn { XCTAssertNotNil(VisualizerRegistry.make(p.visualizerId), p.visualizerId) }
    }

    func testRoundTrip() {
        let p = Array(TestPaths.allPresets().prefix(5))
        let again = try? PresetValidator.parse(PresetValidator.serialize(p))
        XCTAssertEqual(again, p)
    }

    func testRejectsOutOfRange() {
        var p = TestPaths.preset("neon-pulse"); p.visual.params.speed = 99
        XCTAssertTrue(PresetValidator.validate(p).contains { $0.contains("speed") })
        p = TestPaths.preset("neon-pulse"); p.visual.palette.colors = ["red", "#00FF00"]
        XCTAssertFalse(PresetValidator.validate(p).isEmpty)
        p = TestPaths.preset("neon-pulse"); p.schemaVersion = 2
        XCTAssertFalse(PresetValidator.validate(p).isEmpty)
        p = TestPaths.preset("neon-pulse"); p.audio.bandEnabled = [true]
        XCTAssertFalse(PresetValidator.validate(p).isEmpty)
    }

    func testOneBadPresetRejectsTheWholePack() {
        var list = Array(TestPaths.allPresets().prefix(3)); list[1].visual.params.lineWidth = 100
        let data = try! PresetValidator.serialize(list)
        XCTAssertThrowsError(try PresetValidator.parse(data))
    }

    func testHostileFiles() {
        XCTAssertThrowsError(try PresetValidator.parse(Data("not json".utf8)))
        XCTAssertThrowsError(try PresetValidator.parse(Data("{}".utf8)))
        XCTAssertThrowsError(try PresetValidator.parse(Data(count: PresetValidator.maxFileBytes + 1)))
        XCTAssertThrowsError(try PresetValidator.parse(Data("{\"format\":\"Other\",\"schemaVersion\":1,\"presets\":[]}".utf8)))
    }

    func testUnknownFieldsAreIgnoredAndNeverExecuted() {
        var json = String(data: try! PresetValidator.serialize([TestPaths.preset("neon-pulse")]), encoding: .utf8)!
        json = json.replacingOccurrences(of: "\"name\"", with: "\"script\" : \"rm -rf /\",\n        \"name\"")
        XCTAssertNoThrow(try PresetValidator.parse(Data(json.utf8)))
    }

    func testCustomPresetLifecycle() {
        let folder = TestPaths.temp()
        let lib = library(folder)
        var p = lib.builtIn[0]; p.name = "Mine"; p.visual.params.glow = 2.5
        let saved = try! lib.saveCustom(p)
        XCTAssertTrue(saved.id.hasPrefix("custom-"))
        XCTAssertEqual(saved.category, "Custom")
        XCTAssertEqual(lib.custom.count, 1)
        let lib2 = library(folder)                       // survives a restart
        XCTAssertEqual(lib2.custom.first?.visual.params.glow, 2.5)
        var clash = lib.builtIn[0]; clash.name = "Clash" // built-ins can never be overwritten
        let s2 = try! lib.saveCustom(clash)
        XCTAssertNotEqual(s2.id, lib.builtIn[0].id)
        lib.deleteCustom(saved.id)
        XCTAssertEqual(lib.custom.count, 1)
        lib.deleteCustom(lib.builtIn[0].id)              // ignored
        XCTAssertEqual(lib.builtIn.count, 25)
    }

    func testImportExport() {
        let lib = library()
        let file = TestPaths.temp().appendingPathComponent("pack.adpreset")
        XCTAssertNoThrow(try lib.export(ids: [lib.builtIn[0].id, lib.builtIn[1].id], to: file))
        let n = try? lib.importFile(file)
        XCTAssertEqual(n, 2)
        XCTAssertEqual(lib.custom.count, 2)
        XCTAssertEqual(Set(lib.all.map { $0.name }).count, lib.all.count, "names are de-duplicated")
        let bad = TestPaths.temp().appendingPathComponent("bad.adpreset")
        try? Data("{\"nope\":1}".utf8).write(to: bad)
        XCTAssertThrowsError(try lib.importFile(bad))
        XCTAssertEqual(lib.custom.count, 2)
    }

    func testFavouritesPersist() {
        let d = TestPaths.defaults()
        let lib = library(TestPaths.temp(), d)
        lib.toggleFavorite("neon-pulse")
        XCTAssertTrue(library(TestPaths.temp(), d).favorites.contains("neon-pulse"))
    }

    func testHexColours() {
        let c = parseHexColor("#FF8000")
        XCTAssertEqual(c.x, 1, accuracy: 0.01); XCTAssertEqual(c.y, 0.502, accuracy: 0.01); XCTAssertEqual(c.z, 0, accuracy: 0.01)
    }
}
