import Foundation
import simd

// Mirrors docs/PRESET_SCHEMA.md (schema version 1). Only the fields the macOS renderer uses are decoded; unknown members
// are ignored, exactly like the Windows importer. Presets are data, never code.

public struct PresetParams: Codable, Equatable {
    public var speed: Double, reactivity: Double, scale: Double, lineWidth: Double, particleDensity: Double, glow: Double
    public var segments: Int, variant: Int
    public var mirror: Bool
    public var rotation: Double
}

public struct PresetPalette: Codable, Equatable { public var colors: [String] }

public struct PresetBackground: Codable, Equatable {
    public var mode: String, color1: String, color2: String
    public var animationSpeed: Double, audioReactive: Double
}

public struct PresetEffects: Codable, Equatable {
    public var bloom: Bool, bloomAmount: Double, bloomThreshold: Double
    public var vignette: Bool, vignetteAmount: Double
    public var chromaticAberration: Bool, chromaticAmount: Double
    public var filmGrain: Bool, filmGrainAmount: Double
    public var scanlines: Bool, scanlineAmount: Double
    public var colorGrading: Bool, saturation: Double, contrast: Double, brightness: Double
}

public struct PresetVisual: Codable, Equatable {
    public var params: PresetParams
    public var palette: PresetPalette
    public var background: PresetBackground
    public var effects: PresetEffects
}

public struct PresetAudio: Codable, Equatable {
    public var sensitivity: Double, smoothing: Double
    public var bassSensitivity: Double, midSensitivity: Double, trebleSensitivity: Double
    public var frequencyTilt: Double
    public var autoGain: Bool, peakDetection: Bool
    public var bandEnabled: [Bool]

    public func analyzerSettings() -> AnalyzerSettings {
        var s = AnalyzerSettings()
        s.sensitivity = Float(sensitivity); s.smoothing = Float(smoothing)
        s.bassSensitivity = Float(bassSensitivity); s.midSensitivity = Float(midSensitivity); s.trebleSensitivity = Float(trebleSensitivity)
        s.tilt = Float(frequencyTilt); s.autoGain = autoGain; s.peakDetection = peakDetection
        s.bandEnabled = (0..<6).map { $0 < bandEnabled.count ? bandEnabled[$0] : true }
        return s
    }
}

public struct Preset: Codable, Identifiable, Equatable {
    public var schemaVersion: Int
    public var id: String
    public var name: String
    public var category: String
    public var description: String
    public var author: String
    public var tags: [String]
    public var visualizerId: String
    public var visual: PresetVisual
    public var audio: PresetAudio
}

public struct PresetPack: Codable {
    public var format: String
    public var schemaVersion: Int
    public var presets: [Preset]
}

public enum PresetError: Error, LocalizedError {
    case invalid([String])
    case tooLarge
    public var errorDescription: String? {
        switch self {
        case .invalid(let r): return "Preset file rejected: " + r.prefix(5).joined(separator: "; ")
        case .tooLarge: return "Preset file is larger than 512 KB."
        }
    }
}

public enum PresetValidator {
    public static let maxFileBytes = 512 * 1024
    public static let maxPresets = 200

    static func hexOK(_ s: String) -> Bool {
        s.count == 7 && s.hasPrefix("#") && s.dropFirst().allSatisfy { $0.isHexDigit }
    }

    /// Returns every reason the preset is invalid (empty = valid). Ranges are the ones documented in PRESET_SCHEMA.md.
    public static func validate(_ p: Preset) -> [String] {
        var e: [String] = []
        func range(_ name: String, _ v: Double, _ lo: Double, _ hi: Double) {
            if !(v >= lo && v <= hi) { e.append("\(p.name): \(name) \(v) outside \(lo)...\(hi)") }
        }
        if p.schemaVersion != 1 { e.append("\(p.name): unsupported schemaVersion \(p.schemaVersion)") }
        if p.name.isEmpty || p.name.count > 80 { e.append("name must be 1-80 characters") }
        if p.category.isEmpty || p.category.count > 40 { e.append("\(p.name): category must be 1-40 characters") }
        if p.description.count > 600 { e.append("\(p.name): description too long") }
        if p.tags.count > 16 || p.tags.contains(where: { $0.count > 32 }) { e.append("\(p.name): bad tags") }
        let v = p.visual.params
        range("speed", v.speed, 0, 4); range("reactivity", v.reactivity, 0, 3); range("scale", v.scale, 0.2, 3)
        range("lineWidth", v.lineWidth, 0.5, 16); range("particleDensity", v.particleDensity, 0.05, 2); range("glow", v.glow, 0, 4)
        range("segments", Double(v.segments), 0, 128); range("variant", Double(v.variant), 0, 3); range("rotation", v.rotation, -360, 360)
        let colors = p.visual.palette.colors
        if colors.count < 2 || colors.count > 8 || !colors.allSatisfy(hexOK) { e.append("\(p.name): palette needs 2-8 colours as #RRGGBB") }
        let b = p.visual.background
        if !["Solid", "Gradient", "AnimatedGradient", "Starfield", "ParticleField", "Transparent"].contains(b.mode) {
            e.append("\(p.name): unknown background mode \(b.mode)")
        }
        if !hexOK(b.color1) || !hexOK(b.color2) { e.append("\(p.name): background colours must be #RRGGBB") }
        range("animationSpeed", b.animationSpeed, 0, 4); range("audioReactive", b.audioReactive, 0, 4)
        let fx = p.visual.effects
        range("bloomAmount", fx.bloomAmount, 0, 4); range("bloomThreshold", fx.bloomThreshold, 0, 1)
        range("vignetteAmount", fx.vignetteAmount, 0, 1); range("chromaticAmount", fx.chromaticAmount, 0, 0.1)
        range("filmGrainAmount", fx.filmGrainAmount, 0, 1); range("scanlineAmount", fx.scanlineAmount, 0, 1)
        range("saturation", fx.saturation, 0, 3); range("contrast", fx.contrast, 0.2, 3); range("brightness", fx.brightness, -1, 1)
        let a = p.audio
        range("sensitivity", a.sensitivity, 0.05, 8); range("smoothing", a.smoothing, 0, 0.99)
        range("bassSensitivity", a.bassSensitivity, 0, 4); range("midSensitivity", a.midSensitivity, 0, 4)
        range("trebleSensitivity", a.trebleSensitivity, 0, 4); range("frequencyTilt", a.frequencyTilt, -1, 1)
        if a.bandEnabled.count != 6 { e.append("\(p.name): bandEnabled needs 6 entries") }
        return e
    }

    /// Parse a `.adpreset` pack or a single bare preset. If anything is invalid nothing is returned.
    public static func parse(_ data: Data) throws -> [Preset] {
        if data.count > maxFileBytes { throw PresetError.tooLarge }
        let dec = JSONDecoder()
        var list: [Preset]
        do {
            if let pack = try? dec.decode(PresetPack.self, from: data) {
                if pack.format != "AudioDimension.Presets" { throw PresetError.invalid(["unknown format '\(pack.format)'"]) }
                list = pack.presets
            } else {
                list = [try dec.decode(Preset.self, from: data)]
            }
        } catch let e as PresetError { throw e
        } catch { throw PresetError.invalid(["not a valid preset file (\(error.localizedDescription))"]) }
        if list.isEmpty || list.count > maxPresets { throw PresetError.invalid(["a file must contain 1-\(maxPresets) presets"]) }
        let errors = list.flatMap(validate)
        if !errors.isEmpty { throw PresetError.invalid(errors) }
        return list
    }

    public static func serialize(_ presets: [Preset]) throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try enc.encode(PresetPack(format: "AudioDimension.Presets", schemaVersion: 1, presets: presets))
    }
}

public func parseHexColor(_ s: String, alpha: Float = 1) -> SIMD4<Float> {
    var v: UInt64 = 0
    Scanner(string: String(s.dropFirst())).scanHexInt64(&v)
    return SIMD4<Float>(Float((v >> 16) & 255) / 255, Float((v >> 8) & 255) / 255, Float(v & 255) / 255, alpha)
}

/// Built-in presets (shared JSON exported from the Windows app) plus the user's own, stored in Application Support.
public final class PresetLibrary: ObservableObject {
    @Published public private(set) var builtIn: [Preset] = []
    @Published public private(set) var custom: [Preset] = []
    @Published public var favorites: Set<String> = []
    /// Number of built-in presets that need a visualizer the Mac version does not have yet.
    @Published public private(set) var unavailableCount = 0

    public let customFolder: URL
    private let defaults: UserDefaults

    public init(resources: URL?, customFolder: URL, defaults: UserDefaults = .standard) {
        self.customFolder = customFolder
        self.defaults = defaults
        favorites = Set(defaults.stringArray(forKey: "favoritePresets") ?? [])
        if let url = resources?.appendingPathComponent("builtin-presets.json"), let data = try? Data(contentsOf: url),
           let all = try? PresetValidator.parse(data) {
            let supported = Set(VisualizerRegistry.ids)
            builtIn = all.filter { supported.contains($0.visualizerId) }
            unavailableCount = all.count - builtIn.count
        }
        loadCustom()
    }

    public var all: [Preset] { builtIn + custom }
    public func preset(id: String) -> Preset? { all.first { $0.id == id } }
    public var categories: [String] {
        var seen: [String] = []
        for p in all where !seen.contains(p.category) { seen.append(p.category) }
        return seen
    }

    public func toggleFavorite(_ id: String) {
        if favorites.contains(id) { favorites.remove(id) } else { favorites.insert(id) }
        defaults.set(Array(favorites), forKey: "favoritePresets")
    }

    private func loadCustom() {
        try? FileManager.default.createDirectory(at: customFolder, withIntermediateDirectories: true)
        let files = (try? FileManager.default.contentsOfDirectory(at: customFolder, includingPropertiesForKeys: nil)) ?? []
        custom = files.filter { $0.pathExtension == "adpreset" }.sorted { $0.lastPathComponent < $1.lastPathComponent }
            .compactMap { try? Data(contentsOf: $0) }.compactMap { try? PresetValidator.parse($0) }.flatMap { $0 }
            .filter { VisualizerRegistry.ids.contains($0.visualizerId) }
    }

    @discardableResult
    public func saveCustom(_ preset: Preset) throws -> Preset {
        var p = preset
        if !p.id.hasPrefix("custom-") || builtIn.contains(where: { $0.id == p.id }) {
            p.id = "custom-" + UUID().uuidString.prefix(10).lowercased()
        }
        p.category = "Custom"; p.author = p.author.isEmpty ? "Me" : p.author
        let errors = PresetValidator.validate(p)
        if !errors.isEmpty { throw PresetError.invalid(errors) }
        try FileManager.default.createDirectory(at: customFolder, withIntermediateDirectories: true)
        try PresetValidator.serialize([p]).write(to: customFolder.appendingPathComponent("\(p.id).adpreset"), options: .atomic)
        loadCustom()
        return p
    }

    public func deleteCustom(_ id: String) {
        guard id.hasPrefix("custom-") else { return }
        try? FileManager.default.removeItem(at: customFolder.appendingPathComponent("\(id).adpreset"))
        favorites.remove(id)
        loadCustom()
    }

    /// Import a file; every preset becomes a new custom preset (built-ins are never overwritten).
    @discardableResult
    public func importFile(_ url: URL) throws -> Int {
        let presets = try PresetValidator.parse(try Data(contentsOf: url))
        let usable = presets.filter { VisualizerRegistry.ids.contains($0.visualizerId) }
        if usable.isEmpty { throw PresetError.invalid(["none of the presets in this file use a visualizer available on macOS yet"]) }
        for var p in usable {
            p.id = "custom-" + UUID().uuidString.prefix(10).lowercased()
            var name = p.name, n = 2
            while all.contains(where: { $0.name == name }) { name = "\(p.name) (\(n))"; n += 1 }
            p.name = name
            try saveCustom(p)
        }
        return usable.count
    }

    public func export(ids: [String], to url: URL) throws {
        try PresetValidator.serialize(ids.compactMap { preset(id: $0) }).write(to: url, options: .atomic)
    }
}
