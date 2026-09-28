#if canImport(SwiftUI)
  import Foundation
  import SwiftUI

  extension L10n {
    /// The catalogue text, as a SwiftUI `Text`.
    public var text: Text {
      Text(LocalizedStringKey(rawValue), bundle: .module)
    }

    /// The catalogue string, for accessibility labels and announcements.
    public var string: String {
      Bundle.module.localizedString(forKey: rawValue, value: nil, table: "Localizable")
    }

    /// A counted or formatted entry; plural variations follow CLDR rules (UX-DR125).
    public func string(_ arguments: Int...) -> String {
      String(format: string, locale: .current, arguments: arguments.map { $0 as CVarArg })
    }
  }
#endif
