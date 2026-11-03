// Duel Core haptics for iOS: impact feedback through UIKit's feedback generators (the Taptic Engine).
// Called from C# through DllImport("__Internal") (Runtime/Haptics/DuelHaptics.cs). Strength values
// mirror HapticStrength: 0 light, 1 medium, 2 heavy.

import UIKit

@_cdecl("duel_haptics_impact")
public func duel_haptics_impact(_ strength: Int32) {
    let style: UIImpactFeedbackGenerator.FeedbackStyle
    switch strength {
    case 0: style = .light
    case 2: style = .heavy
    default: style = .medium
    }
    // Feedback generators belong to the main thread.
    DispatchQueue.main.async {
        UIImpactFeedbackGenerator(style: style).impactOccurred()
    }
}
