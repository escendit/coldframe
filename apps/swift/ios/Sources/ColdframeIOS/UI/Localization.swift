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

    /// An entry with text placeholders (`%@`), such as a Site name or a time-zone ID.
    public func string(_ arguments: String...) -> String {
      String(format: string, locale: .current, arguments: arguments.map { $0 as CVarArg })
    }
  }
#endif

#if canImport(SwiftUI)
  import Foundation

  extension Copy {
    /// The catalogue entry with its arguments filled in.
    public var string: String {
      let values: [CVarArg] = arguments.map { argument in
        switch argument {
        case .text(let text): text as NSString
        case .number(let number): number
        }
      }
      return values.isEmpty
        ? key.string : String(format: key.string, locale: .current, arguments: values)
    }
  }
#endif

#if canImport(SwiftUI)
  import Foundation

  extension CopyContext {
    /// Copy from the String Catalog, with times told against `now` in `timeZone`.
    public static func catalogue(
      now: Date = Date(), timeZone: TimeZone = .current, locale: Locale = .current
    ) -> CopyContext {
      CopyContext(now: now, timeZone: timeZone, locale: locale, resolve: { $0.string })
    }
  }
#endif
