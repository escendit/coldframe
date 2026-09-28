import Foundation
import Testing

@testable import ColdframeIOS

@Test("UX-DR124 the catalogue has exactly the L10n keys")
func catalogueKeys() throws {
  let keys = Set(try Catalogue.entries().keys.map { $0.components(separatedBy: ".")[0] })
  #expect(keys == Set(L10n.allCases.map(\.rawValue)))
}

@Test("UX-DR125 every counted string has the CLDR one and other forms")
func pluralForms() throws {
  let entries = try Catalogue.entries()
  for key in L10n.plurals {
    #expect(entries["\(key.rawValue).one"] != nil)
    #expect(entries["\(key.rawValue).other"] != nil)
  }
  #expect(Catalogue.format(entries["count_lots_need_water.one"] ?? "", [1]) == "1 Lot needs water")
  #expect(
    Catalogue.format(entries["count_lots_need_water.other"] ?? "", [5]) == "5 Lots need water")
}

@Test("UX-DR130 no exclamation marks, emoji, successfully, OK, fine or all good")
func voiceRules() throws {
  let banned = try NSRegularExpression(
    pattern: #"!|successfully|\bOK\b|\bokay\b|\bfine\b|all good"#, options: .caseInsensitive)
  for (key, value) in try Catalogue.entries() {
    let range = NSRange(value.startIndex..., in: value)
    #expect(banned.firstMatch(in: value, range: range) == nil, "\(key)")
    #expect(!value.unicodeScalars.contains { $0.properties.isEmojiPresentation }, "\(key)")
  }
}

@Test("UX-DR130 uppercase comes from style, never from the string")
func noUppercaseCopy() throws {
  let shouting = try NSRegularExpression(pattern: #"\b\p{Lu}{2,}\b"#)
  for (key, value) in try Catalogue.entries() {
    let range = NSRange(value.startIndex..., in: value)
    #expect(shouting.firstMatch(in: value, range: range) == nil, "\(key)")
  }
}

@Test("UX-DR131 glossary terms are capitalised wherever they appear")
func glossaryTerms() throws {
  let data = try Data(contentsOf: Repo.url("apps/ts/web/src/lib/i18n/glossary.json"))
  let terms =
    (try JSONSerialization.jsonObject(with: data) as? [String: Any])?["terms"] as? [String] ?? []
  #expect(terms.contains("Site"))
  for (key, value) in try Catalogue.entries() {
    for term in terms {
      let pattern = "\\b\(NSRegularExpression.escapedPattern(for: term))s?\\b"
      let expression = try NSRegularExpression(pattern: pattern, options: .caseInsensitive)
      for match in expression.matches(in: value, range: NSRange(value.startIndex..., in: value)) {
        let found = String(value[Range(match.range, in: value)!])
        let initials = found.split(separator: " ").map { $0.first }
        #expect(initials == term.split(separator: " ").map { $0.first }, "\(key): \(found)")
      }
    }
  }
}

@Test("UX-DR124 browser copy says phone on iOS")
func phoneCopy() throws {
  let entries = try Catalogue.entries()
  #expect(entries["appearance_theme_helper"] == "Applies at once and only on this phone.")
  #expect(entries["settings_sign_out_question"] == "Sign out of Coldframe on this phone?")
}
