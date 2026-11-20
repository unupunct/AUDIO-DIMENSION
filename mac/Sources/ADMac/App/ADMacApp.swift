import SwiftUI

@main
struct AudioDimensionApp: App {
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Audio Dimension") {
            RootView()
                .environmentObject(model)
                .frame(minWidth: 1100, minHeight: 700)
                .preferredColorScheme(.dark)
        }
    }
}
