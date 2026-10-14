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
    pattern: #"!|successfully|\bokay\b|\bfine\b|all good"#, options: .caseInsensitive)
  let ok = try NSRegularExpression(pattern: #"\bOK\b"#, options: .caseInsensitive)
  let okKeys = Set(L10n.namesOkStatus.map(\.rawValue))
  for (key, value) in try Catalogue.entries() {
    let range = NSRange(value.startIndex..., in: value)
    #expect(banned.firstMatch(in: value, range: range) == nil, "\(key)")
    if !okKeys.contains(key) {
      #expect(ok.firstMatch(in: value, range: range) == nil, "\(key)")
    }
    #expect(!value.unicodeScalars.contains { $0.properties.isEmojiPresentation }, "\(key)")
  }
}

@Test("UX-DR130 OK names the ok status only: its tile label, its stale and spoken forms, its count")
func okNamesTheStatusOnly() throws {
  let entries = try Catalogue.entries()
  let ok = try NSRegularExpression(pattern: #"\bOK\b"#, options: .caseInsensitive)
  let written = entries.filter { _, value in
    ok.firstMatch(in: value, range: NSRange(value.startIndex..., in: value)) != nil
  }

  #expect(
    Set(written.keys) == [
      "lot_tile_label_ok", "lot_tile_was_ok", "lot_tile_spoken_ok", "lot_tile_spoken_was_ok",
      "garden_count_ok",
    ])
  #expect(Set(written.keys) == Set(L10n.namesOkStatus.map(\.rawValue)))
  // Always the status name, at the end of the entry and in capitals.
  #expect(written.values.allSatisfy { $0.hasSuffix("OK") })
}

@Test("UX-DR130 uppercase comes from style, never from the string")
func noUppercaseCopy() throws {
  let shouting = try NSRegularExpression(pattern: #"\b\p{Lu}{2,}\b"#)
  // Acronyms are words, not style: the Hub's LED (UX-DR94) and its Device ID.
  let acronyms: Set<String> = ["LED", "ID"]
  // "OK" is a word written in capitals, allowed only where the test above allows it.
  let okKeys = Set(L10n.namesOkStatus.map(\.rawValue))
  for (key, value) in try Catalogue.entries() {
    let allowed = okKeys.contains(key) ? acronyms.union(["OK"]) : acronyms
    let range = NSRange(value.startIndex..., in: value)
    let found = shouting.matches(in: value, range: range).compactMap {
      Range($0.range, in: value).map { String(value[$0]) }
    }
    #expect(found.allSatisfy { allowed.contains($0) }, "\(key)")
  }
}

@Test("UX-DR131 glossary terms are capitalised wherever they appear")
func glossaryTerms() throws {
  let data = try Data(contentsOf: Repo.url("apps/ts/web/src/lib/i18n/glossary.json"))
  let terms =
    (try JSONSerialization.jsonObject(with: data) as? [String: Any])?["terms"] as? [String] ?? []
  #expect(terms.contains("Site"))
  // The glossary holds nouns. One string uses a glossary word as a verb, in the copy UX-DR46
  // gives for the Pause sheet; only that key and that word are let through.
  let verbs = ["pause_until_helper": "pause"]
  let entries = try Catalogue.entries()
  #expect(entries["pause_until_helper"] == "Leave empty to pause until someone resumes.")
  for (key, value) in entries {
    for term in terms {
      let pattern = "\\b\(NSRegularExpression.escapedPattern(for: term))s?\\b"
      let expression = try NSRegularExpression(pattern: pattern, options: .caseInsensitive)
      for match in expression.matches(in: value, range: NSRange(value.startIndex..., in: value)) {
        let found = String(value[Range(match.range, in: value)!])
        if verbs[key] == found { continue }
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

/// The UX-DRs of the stories whose acceptance asks for a test named after each one. A test
/// covers an ID when its name starts with it, alone or in a run of IDs.
private let coveredUxDrs: [String: [Int]] = [
  "4.7 Lot status and the Site overview": [
    12, 17, 18, 19, 20, 24, 77, 79, 80, 97, 98, 99, 106, 107, 112, 128, 129,
  ],
  "4.8 Lot detail with history and Device status": [27, 28, 29, 30, 32, 33, 63, 78, 98],
  "5.4 Set Thresholds in the app and see them on the chart": [5, 32, 33, 45, 69, 84, 91],
  "6.2 See Alerts in the apps": [14, 25, 26, 64, 82, 98],
  "6.3 My notification settings and the Site Reminder cadence": [47, 48, 49, 50, 72],
  "6.5 Push notifications on my phone": [88, 115, 120, 121, 122],
  "7.2 Silence in the apps and Silence Window settings": [30, 31, 78, 81, 84, 117],
  "7.3 Low-battery Alert": [25, 26],
  "7.4 Uncalibrated Sensor Alert": [25, 26],
  "8.2 Pause and resume in the apps": [46, 70, 83],
  "9.1 Invite someone to my Site": [51, 52, 73, 76, 84, 90],
  "9.2 Change and remove Roles, always keeping an Owner": [51, 73, 84, 113],
]

@Test("UX-DR124 every UX-DR of a listed story has an iOS test whose name starts with its ID")
func uxDrCoverage() throws {
  let name = try NSRegularExpression(pattern: #"@Test\(\s*"((?:UX-DR\d+ )+)"#)
  var named: Set<Int> = []
  for file in Repo.swiftSources("tests/swift/ios") {
    let text = try String(contentsOf: file, encoding: .utf8)
    for match in name.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
      guard let ids = Range(match.range(at: 1), in: text) else { continue }
      for id in text[ids].split(separator: " ") {
        if let number = Int(id.dropFirst("UX-DR".count)) { named.insert(number) }
      }
    }
  }
  for (story, ids) in coveredUxDrs {
    for id in ids {
      #expect(named.contains(id), "UX-DR\(id) of story \(story) has no test named after it")
    }
  }
}

@Test("UX-DR124 UX-DR91 the Threshold copy is in the catalogue with its placeholders")
func thresholdCopy() throws {
  let entries = try Catalogue.entries()
  #expect(entries["thresholds_low_not_below_high"] == "Low must stay below high.")
  #expect(
    entries["thresholds_notice_forbidden"]
      == "You can't change this on %1$@. Ask an Owner or Administrator.")
  #expect(entries["lot_detail_chart_legend"] == "solid bar = below %1$@ %%")
  for kind in ThresholdsNoticeKind.allCases {
    #expect(entries[kind.message.rawValue] != nil, "\(kind)")
  }
}

@Test("UX-DR124 UX-DR82 the Alerts copy is in the catalogue: empty state, groups, titles")
func alertsCatalogueCopy() throws {
  let entries = try Catalogue.entries()
  #expect(entries["alerts_empty"] == "No open Alerts.")
  #expect(entries["alert_title_needs_water"] == "%1$@ needs water")
  #expect(entries["alert_title_too_wet"] == "%1$@ too wet")
  #expect(entries["alert_title_too_low"] == "%1$@ %2$@ too low")
  #expect(entries["alert_title_too_high"] == "%1$@ %2$@ too high")
  #expect(entries["alert_spoken_open"] == "%1$@, since %2$@")
  #expect(entries["alert_spoken_closed"] == "%1$@, since %2$@, closed %3$@")
  for group in AlertGroupKind.allCases {
    #expect(entries[group.title.rawValue] != nil, "\(group)")
  }
  for kind in AlertsNoticeKind.allCases {
    #expect(entries[kind.message.rawValue] != nil, "\(kind)")
  }
  #expect(entries["alerts_unreachable"] == entries["devices_unreachable"])
}

/// The entries of the Android `strings.xml` and of the iOS catalogue whose key starts with one
/// of `prefixes`, with the placeholders and escapes of both written the same way.
private func sharedCopy(
  _ prefixes: [String]
) throws -> (android: [String: String], ios: [String: String]) {
  let xml = try Repo.text("apps/kt/android/src/main/res/values/strings.xml")
  let names = "(?:" + prefixes.joined(separator: "|") + ")"
  let single = try NSRegularExpression(
    pattern: #"<string name="("# + names + #"[a-z0-9_]*)">(.*?)</string>"#)
  let plural = try NSRegularExpression(
    pattern: #"<plurals name="("# + names + #"[a-z0-9_]*)">(.*?)</plurals>"#,
    options: .dotMatchesLineSeparators)
  let item = try NSRegularExpression(pattern: #"<item quantity="(\w+)">(.*?)</item>"#)
  func captures(_ expression: NSRegularExpression, _ text: String) -> [(String, String)] {
    expression.matches(in: text, range: NSRange(text.startIndex..., in: text)).compactMap {
      guard let first = Range($0.range(at: 1), in: text),
        let second = Range($0.range(at: 2), in: text)
      else { return nil }
      return (String(text[first]), String(text[second]))
    }
  }
  /// Android numbers its placeholders (`%1$s`, `%1$d`) and escapes apostrophes.
  func words(_ value: String) -> String {
    var result = value.replacingOccurrences(of: "\\'", with: "'")
    for (android, ios) in [("$s", "$@"), ("$d", "$lld"), ("%lld", "%1$lld"), ("%@", "%1$@")] {
      result = result.replacingOccurrences(of: android, with: ios)
    }
    return result
  }
  var android: [String: String] = [:]
  for (key, value) in captures(single, xml) { android[key] = words(value) }
  for (key, body) in captures(plural, xml) {
    for (form, value) in captures(item, body) { android["\(key).\(form)"] = words(value) }
  }
  let ios = try Catalogue.entries().filter { entry in
    prefixes.contains { entry.key.hasPrefix($0) }
  }.mapValues(words)
  return (android, ios)
}

@Test("UX-DR124 the Alerts strings are the Android ones, key for key and word for word")
func alertsCatalogueMatchesAndroid() throws {
  let copy = try sharedCopy(["alert", "nav_alerts"])

  #expect(copy.android.count > 10)
  #expect(copy.android == copy.ios)
}

@Test("UX-DR124 UX-DR72 the notification settings strings are the Android ones, word for word")
func notificationsCatalogueMatchesAndroid() throws {
  let copy = try sharedCopy([
    "notifications_", "reminders_", "settings_notifications", "site_settings_reminder",
    "time_zone_",
  ])

  #expect(copy.android.count >= 40)
  #expect(copy.android == copy.ios)
  #expect(copy.ios["time_zone_helper"] == "Used for your Notification Window.")
  #expect(copy.ios["notifications_mute"] == "Mute %1$@")
  #expect(copy.ios["notifications_window_range"] == "%1$@ to %2$@")
  // The window is told to a person: no Silence Window, no "Never".
  #expect(!copy.ios.values.contains { $0.contains("Silence Window") || $0.contains("Never") })
}

@Test("UX-DR124 UX-DR88 UX-DR122 the push strings are the Android ones, word for word")
func pushCatalogueMatchesAndroid() throws {
  let copy = try sharedCopy(["push_"])

  #expect(
    Set(copy.ios.keys) == [
      "push_why", "push_why_continue", "push_off", "push_open_settings", "push_channel_alerts",
    ])
  #expect(copy.android == copy.ios)
  #expect(copy.ios["push_why"] == "Coldframe tells you when a Lot needs water.")
  #expect(
    copy.ios["push_off"]
      == "Notifications are off for Coldframe on this phone. You won't get Alerts.")
  for notice in PushNoticeKind.allCases {
    #expect(copy.ios[notice.message.rawValue] != nil, "\(notice)")
    #expect(copy.ios[notice.actionLabel.rawValue] != nil, "\(notice)")
  }
}

@Test("UX-DR124 UX-DR72 every notification settings notice has its catalogue entry")
func notificationsNoticeCopy() throws {
  let entries = try Catalogue.entries()
  let controls: [NotificationControlKind?] = NotificationControlKind.allCases + [nil]
  for kind in NotificationSettingsNoticeKind.allCases {
    for control in controls {
      #expect(entries[kind.message(control: control).rawValue] != nil, "\(kind)")
    }
  }
  #expect(
    entries["notifications_notice_site_gone"]
      == "%1$@ is no longer one of your Sites. Nothing was changed.")
}

@Test(
  "UX-DR124 UX-DR81 UX-DR117 the silence strings are the Android ones, key for key and word for word"
)
func silenceCatalogueMatchesAndroid() throws {
  let copy = try sharedCopy([
    "garden_hub_silent", "lot_detail_hub_silent", "devices_silen", "alert_title_",
  ])

  #expect(copy.android.count >= 30)
  #expect(copy.android == copy.ios)
  #expect(
    copy.ios["garden_hub_silent"] == "Hub %1$@ silent for %2$@ — Lots behind it can't be read.")
  #expect(
    copy.ios["lot_detail_hub_silent_named"] == "Hub %1$@ is silent; Lots behind it can't be read.")
  #expect(copy.ios["devices_silent"] == "Silent %1$@")
  #expect(copy.ios["alert_title_silent_for"] == "Node on Lot '%1$@' silent for %2$@")
  #expect(copy.ios["alert_title_hub_silent_for"] == "Hub %1$@ silent for %2$@")
  #expect(copy.ios["alert_title_node_silent_for"] == "Node %1$@ silent for %2$@")
  // Story 7.3: the low-battery titles are under the same prefix, and in parity too.
  #expect(
    copy.ios["alert_title_battery_level"] == "Node battery %1$@ %%, not charging — Lot '%2$@'")
  #expect(copy.ios["alert_title_node_battery"] == "Node %1$@ battery low")
}

@Test("UX-DR124 UX-DR31 every Silence Window notice and unit has its catalogue entry")
func silenceWindowCopy() throws {
  let entries = try Catalogue.entries()
  for kind in SilenceWindowNoticeKind.allCases {
    #expect(entries[kind.message.rawValue] != nil, "\(kind)")
  }
  for unit in SilenceWindowUnitKind.allCases {
    #expect(entries[unit.label.rawValue] != nil, "\(unit)")
  }
  #expect(
    SilenceWindowUnitKind.allCases.map { entries[$0.label.rawValue] } == ["min", "h", "d"])
  #expect(
    entries["site_settings_forbidden"]
      == "You can't change this on %@. Ask an Owner or Administrator.")
}

@Test(
  "UX-DR124 UX-DR46 UX-DR70 UX-DR83 the Pause strings are the Android ones, key for key and word for word"
)
func pauseCatalogueMatchesAndroid() throws {
  let copy = try sharedCopy([
    "pause_", "garden_site_paused", "devices_pause_", "devices_resume_", "site_menu_",
    "lot_detail_resume_site", "lot_tile_label_paused",
  ])

  #expect(copy.android.count >= 30)
  #expect(copy.android == copy.ios)
  #expect(copy.ios["pause_title"] == "Pause")
  #expect(copy.ios["pause_scope_device"] == "This Device")
  #expect(copy.ios["pause_scope_site"] == "Whole Site")
  #expect(copy.ios["pause_until"] == "Until")
  #expect(copy.ios["pause_until_helper"] == "Leave empty to pause until someone resumes.")
  #expect(copy.ios["pause_until_refused"] == "Choose a date after today.")
  #expect(copy.ios["pause_primary"] == "Pause %1$@")
  #expect(copy.ios["pause_member_notice"] == "Only Owners and Administrators can resume it.")
  // UX-DR83: the first sentence of the product string, without an attribution.
  #expect(copy.ios["garden_site_paused"] == "No Alerts are sent while the Site is paused.")
  #expect(!copy.ios.values.contains { $0.contains("Paused by %") })
  #expect(copy.ios["lot_detail_resume_site"] == "Resume the Site to resume this Node")
  #expect(copy.ios["devices_resume_site_hub"] == "Resume the Site to resume this Hub")
  #expect(copy.ios["site_menu_pause"] == "Pause %1$@")
  #expect(copy.ios["site_menu_resume"] == "Resume %1$@")
  #expect(copy.ios["site_menu_needs_server"] == "Needs your Server")
}

@Test("UX-DR124 UX-DR46 every Pause scope and notice has its catalogue entry")
func pauseNoticeCopy() throws {
  let entries = try Catalogue.entries()
  for scope in PauseScopeKind.allCases {
    #expect(entries[scope.label.rawValue] != nil, "\(scope)")
  }
  for kind in PauseNoticeKind.allCases {
    for unreachable in [L10n.noticeUnreachable, .devicesUnreachable] {
      #expect(entries[kind.message(unreachable: unreachable).rawValue] != nil, "\(kind)")
    }
  }
  // The 403 copy is the one every Role-gated control shares (UX-DR84).
  #expect(
    PauseNoticeKind.forbidden.message(unreachable: .noticeUnreachable) == .siteSettingsForbidden)
  #expect(
    entries["site_settings_forbidden"]
      == "You can't change this on %@. Ask an Owner or Administrator.")
}
