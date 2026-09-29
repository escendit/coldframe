#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI
  #if canImport(UIKit)
    import UIKit
  #endif

  /// What Add a Hub can ask of the core; the views forward taps and render what comes back.
  @MainActor
  public struct HubSetupActions {
    public var open: () -> Void
    public var close: () -> Void
    public var recheckRadio: () -> Void
    public var announcing: (Bool) -> Void
    public var back: () -> Void
    public var leave: () -> Void
    public var confirmLeave: () -> Void
    public var stayInFlow: () -> Void
    public var select: (String) -> Void
    public var continueFromScan: () -> Void
    public var setCode: (String) -> Void
    public var submitCode: () -> Void
    public var continueFromCode: () -> Void
    public var chooseNetwork: (String) -> Void
    public var chooseOtherNetwork: () -> Void
    public var setOtherSsid: (String) -> Void
    public var setPassword: (String) -> Void
    public var continueFromWifi: () -> Void
    public var chooseSite: (String) -> Void
    public var retryKey: () -> Void
    public var start: () -> Void
    public var outcomeAction: (OutcomeActionKind) -> Void

    public init(service: HubSetupService) {
      open = { service.open() }
      close = { service.close() }
      recheckRadio = { service.recheckRadio() }
      announcing = { service.announcing($0) }
      back = { service.back() }
      leave = { service.leave() }
      confirmLeave = { service.confirmLeave() }
      stayInFlow = { service.stayInFlow() }
      select = { service.select(candidateId: $0) }
      continueFromScan = { service.continueFromScan() }
      setCode = { service.setCode($0) }
      submitCode = { service.submitCode() }
      continueFromCode = { service.continueFromCode() }
      chooseNetwork = { service.chooseNetwork($0) }
      chooseOtherNetwork = { service.chooseOtherNetwork() }
      setOtherSsid = { service.setOtherSsid($0) }
      setPassword = { service.setPassword($0) }
      continueFromWifi = { service.continueFromWifi() }
      chooseSite = { service.chooseSite($0) }
      retryKey = { service.retryKey() }
      start = { service.start() }
      outcomeAction = { service.outcomeAction($0) }
    }

    private init() {
      open = {}
      close = {}
      recheckRadio = {}
      announcing = { _ in }
      back = {}
      leave = {}
      confirmLeave = {}
      stayInFlow = {}
      select = { _ in }
      continueFromScan = {}
      setCode = { _ in }
      submitCode = {}
      continueFromCode = {}
      chooseNetwork = { _ in }
      chooseOtherNetwork = {}
      setOtherSsid = { _ in }
      setPassword = { _ in }
      continueFromWifi = {}
      chooseSite = { _ in }
      retryKey = {}
      start = {}
      outcomeAction = { _ in }
    }

    public static let none = HubSetupActions()
  }

  /// Add a Hub (UX-DR66): the five steps in the Setup flow shell, then an outcome screen, full
  /// screen over the tab shell. The idle timer is off while the flow is open; VoiceOver focus
  /// moves to the step title or headline; announcements are made once each, and while one is
  /// read the core's progress timeout waits (UX-DR103, UX-DR105).
  public struct AddHubFlowView: View {
    let presentation: HubSetupPresentation
    let actions: HubSetupActions
    @AccessibilityFocusState private var titleFocused: Bool
    /// The announcement being read, if any; only its own finish or fallback ends the hold.
    @State private var speakingId: Int?
    @State private var speakingText: String?
    @Environment(\.palette) private var palette
    @Environment(\.scenePhase) private var scenePhase

    public init(presentation: HubSetupPresentation, actions: HubSetupActions) {
      self.presentation = presentation
      self.actions = actions
    }

    public var body: some View {
      Group {
        if let outcome = presentation.outcome {
          OutcomeView(
            outcome: outcome, onAction: actions.outcomeAction, titleFocused: $titleFocused)
        } else {
          shell
        }
      }
      .onAppear {
        keepAwake(presentation.keepAwake)
        titleFocused = true
      }
      .onDisappear { keepAwake(false) }
      .onChange(of: presentation.step) { titleFocused = true }
      .onChange(of: presentation.outcome?.kind) { titleFocused = true }
      .onChange(of: presentation.announcement?.id) { announce() }
      .onChange(of: scenePhase) { _, phase in
        if phase == .active { actions.recheckRadio() }
      }
      .confirmationDialog(
        Text(verbatim: presentation.leaveQuestion.string),
        isPresented: Binding(
          get: { presentation.confirmingLeave }, set: { if !$0 { actions.stayInFlow() } }),
        titleVisibility: .visible
      ) {
        Button(role: .destructive, action: actions.confirmLeave) { L10n.setupLeaveConfirm.text }
        Button(role: .cancel, action: actions.stayInFlow) { L10n.setupLeaveStay.text }
      } message: {
        L10n.setupLeaveDetail.text
      }
      #if canImport(UIKit)
        .onReceive(
          NotificationCenter.default.publisher(
            for: UIAccessibility.announcementDidFinishNotification)
        ) { notification in
          let finished =
            notification.userInfo?[UIAccessibility.announcementStringValueUserInfoKey] as? String
          if let speakingText, finished == nil || finished == speakingText { release(speakingId) }
        }
      #endif
    }

    private var shell: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          PrimaryButton(
            presentation.backLabel, variant: .ghost,
            action: presentation.showsCancel ? actions.leave : actions.back)
          counter
          Text(verbatim: presentation.title.string).role(Typography.headline)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityAddTraits(.isHeader)
            .accessibilityFocused($titleFocused)
          step
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
    }

    /// "01 / 05": the current number in `primary-text`, the total in `text-helper`.
    private var counter: some View {
      HStack(alignment: .firstTextBaseline, spacing: Spacing.step3) {
        Text(verbatim: presentation.counterCurrent).role(Typography.stepCounter)
          .foregroundStyle(palette.primaryText)
        Text(verbatim: presentation.counterTotal.string).role(Typography.stepCounter)
          .foregroundStyle(palette.textHelper)
      }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: presentation.counterDescription.string))
    }

    @ViewBuilder
    private var step: some View {
      switch presentation.step {
      case .scan: ScanStepView(presentation: presentation, actions: actions)
      case .code: CodeStepView(presentation: presentation, actions: actions)
      case .wifi: WifiStepView(presentation: presentation, actions: actions)
      case .site: SiteStepView(presentation: presentation, actions: actions)
      case .progress: SetupProgressPanel(presentation: presentation)
      }
    }

    private func keepAwake(_ on: Bool) {
      #if canImport(UIKit)
        UIApplication.shared.isIdleTimerDisabled = on
      #endif
    }

    private func announce() {
      guard let announcement = presentation.announcement,
        let message = announcement.message(siteName: presentation.selectedSiteName)
      else { return }
      let text: String
      if message.parts.isEmpty {
        text = message.copy.string
      } else if announcement.kind == .error {
        let title = message.parts[0].string.trimmingCharacters(in: CharacterSet(charactersIn: "."))
        text = Copy(message.copy.key, .text(title), .text(message.parts[1].string)).string
      } else {
        text =
          Copy(
            key: message.copy.key,
            arguments: message.copy.arguments + [.text(message.parts[0].string)]
          ).string
      }
      var attributed = AttributedString(text)
      if announcement.isAssertive {
        attributed.accessibilitySpeechAnnouncementPriority = .high
      }
      #if canImport(UIKit)
        if UIAccessibility.isVoiceOverRunning {
          let id = announcement.id
          speakingId = id
          speakingText = text
          actions.announcing(true)
          // The finished notification ends the hold; this bounds it if it never comes.
          DispatchQueue.main.asyncAfter(deadline: .now() + readingTime(of: text) * 2) {
            release(id)
          }
        }
      #endif
      AccessibilityNotification.Announcement(attributed).post()
    }

    /// Ends the hold only when [id] is still the announcement being read.
    private func release(_ id: Int?) {
      guard let id, id == speakingId else { return }
      speakingId = nil
      speakingText = nil
      actions.announcing(false)
    }
  }

  /// Step 1: candidate tiles strongest first, the Bluetooth and no-Hub notices, "Still
  /// scanning…" and the primary action naming the Hub.
  struct ScanStepView: View {
    let presentation: HubSetupPresentation
    let actions: HubSetupActions
    @Environment(\.palette) private var palette
    @Environment(\.openURL) private var openURL

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.addHubScanIntro.text.role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        if let notice = presentation.radio.notice {
          InlineNotice(
            message: notice, announcement: .assertive,
            action: presentation.radio.opensSettings ? (.addHubOpenSettings, openSettings) : nil)
        }
        if presentation.noHubYet {
          InlineNotice(message: .addHubNoHub)
        }
        VStack(spacing: Spacing.tileGap) {
          ForEach(presentation.candidates) { candidate in
            CandidateTileView(candidate: candidate) { actions.select(candidate.id) }
          }
        }
        if presentation.showsStillScanning {
          L10n.addHubStillScanning.text.role(Typography.body).foregroundStyle(palette.textSecondary)
        }
        if let action = presentation.scanAction {
          CopyButton(action, action: actions.continueFromScan)
        }
      }
    }

    private func openSettings() {
      #if canImport(UIKit)
        if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
      #endif
    }
  }

  /// Device candidate tile (UX-DR37): one element "Hub 3F2A, strong signal", selected with a
  /// 2 pt `primary-text` border, a checkmark and the selected trait.
  struct CandidateTileView: View {
    let candidate: CandidatePresentation
    let onSelect: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Button(action: onSelect) {
        HStack(spacing: Spacing.step4) {
          VStack(alignment: .leading, spacing: Spacing.step2) {
            Text(verbatim: candidate.name.string).role(Typography.metaMono)
              .foregroundStyle(palette.textPrimary)
            candidate.signal.label.text.role(Typography.statusLabel)
              .foregroundStyle(palette.textSecondary)
          }
          Spacer(minLength: 0)
          if candidate.isSelected {
            CarbonIconShape(.checkmark).fill(palette.primaryText).frame(width: 20, height: 20)
              .accessibilityHidden(true)
          }
        }
        .padding(Spacing.tilePadding)
        .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .leading)
        .background(palette.layer01)
        .overlay {
          if candidate.isSelected {
            Rectangle().strokeBorder(palette.primaryText, lineWidth: 2)
          }
        }
        .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(
        Text(
          verbatim: Copy(
            .addHubCandidateDescription, .text(candidate.hub), .text(candidate.signal.label.string)
          ).string)
      )
      .accessibilityAddTraits(candidate.isSelected ? [.isButton, .isSelected] : .isButton)
    }
  }

  /// Step 2: the setup code field (UX-DR41) with its "Accepted" chip, then the Device ID.
  struct CodeStepView: View {
    let presentation: HubSetupPresentation
    let actions: HubSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.addHubCodeIntro.text.role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        SetupCodeFieldView(presentation: presentation, onChange: actions.setCode)
        if let deviceId = presentation.deviceId {
          VStack(alignment: .leading, spacing: Spacing.step2) {
            L10n.addHubDeviceId.text.role(Typography.helper).foregroundStyle(palette.textHelper)
            Text(verbatim: deviceId).role(Typography.metaMono).foregroundStyle(palette.textPrimary)
          }
        }
        PrimaryButton(
          presentation.codeAction, isEnabled: presentation.isCodeActionEnabled,
          action: presentation.codeAccepted ? actions.continueFromCode : actions.submitCode)
      }
    }
  }

  /// Setup code field (UX-DR41): `meta-mono`, auto-uppercase, no autocorrect; the core
  /// normalizes what was typed. "Accepted" chip on success; the reason replaces the helper.
  struct SetupCodeFieldView: View {
    let presentation: HubSetupPresentation
    let onChange: (String) -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      let error = presentation.codeErrorMessage?.string
      VStack(alignment: .leading, spacing: Spacing.step3) {
        L10n.addHubCodeLabel.text.role(Typography.body).foregroundStyle(palette.textSecondary)
        HStack(spacing: Spacing.step3) {
          TextField(
            L10n.addHubCodeLabel.string,
            text: Binding(get: { presentation.codeText }, set: { onChange($0) })
          )
          .role(Typography.metaMono)
          .foregroundStyle(palette.textPrimary)
          .autocorrectionDisabled()
          #if canImport(UIKit)
            .textInputAutocapitalization(.characters)
          #endif
          .disabled(presentation.codeAccepted)
          .accessibilityHint(error ?? L10n.addHubCodeHelper.string)
          if error != nil {
            CarbonIconShape(.errorFilled).fill(palette.supportErrorText).frame(
              width: 16, height: 16
            )
            .accessibilityHidden(true)
          }
          if presentation.codeAccepted {
            HStack(spacing: Spacing.step2) {
              CarbonIconShape(.checkmark).fill(palette.textPrimary).frame(width: 16, height: 16)
                .accessibilityHidden(true)
              L10n.addHubCodeAccepted.text.role(Typography.statusLabel)
                .foregroundStyle(palette.textPrimary)
            }
            .padding(Spacing.step3)
            .background(palette.layer01)
          }
        }
        .padding(.leading, Spacing.step5)
        .padding(.trailing, Spacing.step3)
        .frame(minHeight: TouchTarget.control)
        .background(palette.field01)
        .overlay(alignment: .bottom) {
          if error == nil { Rectangle().fill(palette.borderStrong).frame(height: 1) }
        }
        .overlay {
          if error != nil { Rectangle().strokeBorder(palette.supportError, lineWidth: 2) }
        }
        Text(verbatim: error ?? L10n.addHubCodeHelper.string).role(Typography.helper)
          .foregroundStyle(error == nil ? palette.textHelper : palette.supportErrorText)
          .fixedSize(horizontal: false, vertical: true)
      }
    }
  }

  /// Step 3: networks as the Hub hears them (UX-DR42), "Other network", the password.
  struct WifiStepView: View {
    let presentation: HubSetupPresentation
    let actions: HubSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        if let networks = presentation.networks {
          Text(verbatim: Copy(.addHubWifiIntro, .text(presentation.hub)).string)
            .role(Typography.body).foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
          VStack(spacing: Spacing.tileGap) {
            ForEach(networks) { row in
              WifiNetworkRowView(row: row) { actions.chooseNetwork(row.ssid) }
            }
          }
          PrimaryButton(.addHubWifiOther, variant: .ghost, action: actions.chooseOtherNetwork)
          if presentation.otherNetwork {
            TextInputField(
              label: L10n.addHubWifiSsid.string,
              value: Binding(get: { presentation.ssid }, set: { actions.setOtherSsid($0) }),
              helper: L10n.addHubWifiSsidHelper.string,
              error: ssidError)
          } else if let ssidError {
            InlineNotice(message: .addHubWifiSsidBlank, announcement: .assertive)
              .accessibilityLabel(Text(verbatim: ssidError))
          }
          if presentation.showsPassword {
            TextInputField(
              label: L10n.addHubWifiPassword.string,
              value: Binding(get: { presentation.password }, set: { actions.setPassword($0) }),
              helper: L10n.addHubWifiPasswordHelper.string,
              error: presentation.wifiError == .passwordTooLong
                ? WifiErrorKind.passwordTooLong.message.string : nil,
              isPassword: true)
          }
          if let action = presentation.wifiAction {
            CopyButton(action, action: actions.continueFromWifi)
          }
        } else {
          Text(verbatim: Copy(.addHubWifiWaiting, .text(presentation.hub)).string)
            .role(Typography.body).foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }

    private var ssidError: String? {
      guard let error = presentation.wifiError, error != .passwordTooLong else { return nil }
      return error.message.string
    }
  }

  /// Wi-Fi network row (UX-DR42): SSID left, security right in `meta-mono`; a WPA3-only or
  /// other unsupported network is hatched on a dashed border, disabled, with its reason.
  struct WifiNetworkRowView: View {
    let row: NetworkRowPresentation
    let onSelect: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Button(action: onSelect) {
        VStack(alignment: .leading, spacing: Spacing.step2) {
          HStack(spacing: Spacing.step3) {
            Text(verbatim: row.ssid).role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
              .plate(row.isHatched, palette)
            Spacer(minLength: 0)
            row.security.label.text.role(Typography.metaMono).foregroundStyle(palette.textSecondary)
              .plate(row.isHatched, palette)
            if row.isSelected {
              CarbonIconShape(.checkmark).fill(palette.primaryText).frame(width: 20, height: 20)
                .accessibilityHidden(true)
            }
          }
          if let reason = row.reason {
            reason.text.role(Typography.helper).foregroundStyle(palette.textPrimary)
              .fixedSize(horizontal: false, vertical: true)
              .plate(true, palette)
          }
        }
        .padding(Spacing.step4)
        .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .leading)
        .background {
          if row.isHatched { Hatch(palette: palette) } else { palette.background }
        }
        .overlay {
          if row.isHatched {
            Rectangle().strokeBorder(
              palette.color(ColorTokens.statusUnknownBorder),
              style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
          } else {
            Rectangle().strokeBorder(
              row.isSelected ? palette.primaryText : palette.borderSubtle,
              lineWidth: row.isSelected ? 2 : 1)
          }
        }
        .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
      .disabled(!row.isSupported)
      .accessibilityElement(children: .combine)
      .accessibilityAddTraits(row.isSelected ? .isSelected : [])
    }
  }

  /// 135° hatch lines on the hatch ground (DESIGN.md Shapes).
  struct Hatch: View {
    let palette: ColdframePalette

    var body: some View {
      Canvas { context, size in
        context.fill(
          Path(CGRect(origin: .zero, size: size)),
          with: .color(palette.color(ColorTokens.statusHatchGround)))
        var x = -size.height
        while x < size.width {
          var line = Path()
          line.move(to: CGPoint(x: x, y: size.height))
          line.addLine(to: CGPoint(x: x + size.height, y: 0))
          context.stroke(
            line, with: .color(palette.color(ColorTokens.statusHatchLine)), lineWidth: 1.5)
          x += 8
        }
      }
      .accessibilityHidden(true)
    }
  }

  extension View {
    /// Text on a hatched row sits on a solid plate.
    fileprivate func plate(_ on: Bool, _ palette: ColdframePalette) -> some View {
      background(on ? palette.color(ColorTokens.statusHatchGround) : Color.clear)
    }
  }

  /// Step 4: the Site and the checked fingerprint of the Server's enrolment key.
  struct SiteStepView: View {
    let presentation: HubSetupPresentation
    let actions: HubSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        Text(verbatim: Copy(.addHubSiteIntro, .text(presentation.hub)).string)
          .role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        if presentation.sites.count > 1 {
          ForEach(presentation.sites) { site in
            Button {
              actions.chooseSite(site.id)
            } label: {
              HStack {
                Text(verbatim: site.name).role(Typography.bodyLg).foregroundStyle(
                  palette.textPrimary)
                Spacer(minLength: 0)
                if site.isSelected {
                  CarbonIconShape(.checkmark).fill(palette.primaryText).frame(width: 20, height: 20)
                    .accessibilityHidden(true)
                }
              }
              .frame(minHeight: TouchTarget.control)
              .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(site.isSelected ? .isSelected : [])
          }
        } else if let site = presentation.sites.first {
          Text(verbatim: site.name).role(Typography.section).foregroundStyle(palette.textPrimary)
        }
        VStack(alignment: .leading, spacing: Spacing.step2) {
          L10n.addHubFingerprintLabel.text.role(Typography.body).foregroundStyle(
            palette.textSecondary)
          if let fingerprint = presentation.groupedFingerprint {
            Text(verbatim: fingerprint).role(Typography.metaMono).foregroundStyle(
              palette.textPrimary
            )
            .fixedSize(horizontal: false, vertical: true)
            Text(verbatim: Copy(.addHubFingerprintHelper, .text(presentation.hub)).string)
              .role(Typography.helper).foregroundStyle(palette.textHelper)
              .fixedSize(horizontal: false, vertical: true)
          } else if let notice = presentation.keyNotice {
            InlineNotice(
              message: notice.message, announcement: .assertive,
              action: (.noticeTryAgain, actions.retryKey))
          } else {
            L10n.addHubFingerprintLoading.text.role(Typography.body)
              .foregroundStyle(palette.textSecondary)
          }
        }
        if let action = presentation.siteAction {
          CopyButton(action, action: actions.start)
        }
      }
    }
  }

  /// Setup progress (UX-DR40): four segments as one progress element, elapsed time (never
  /// announced) and "Usually under a minute." Icons are static under Reduce Motion and always.
  struct SetupProgressPanel: View {
    let presentation: HubSetupPresentation
    @Environment(\.palette) private var palette

    var body: some View {
      let description = presentation.progressDescription
      VStack(alignment: .leading, spacing: Spacing.step5) {
        VStack(spacing: Spacing.step2) {
          ForEach(presentation.segments) { segment in
            segmentView(segment)
          }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
          Text(
            verbatim: Copy(
              .addHubProgressDescription, .number(description.position),
              .number(description.total), .text(description.activity.string)
            ).string)
        )
        .accessibilityValue(
          Text(verbatim: "\(presentation.progressReached)/\(description.total)"))
        Text(verbatim: presentation.elapsed.string).role(Typography.metaMono)
          .foregroundStyle(palette.textSecondary)
        L10n.addHubUsually.text.role(Typography.body).foregroundStyle(palette.textSecondary)
      }
    }

    private func segmentView(_ segment: ProgressSegmentPresentation) -> some View {
      let ink: Color = segment.state == .pending ? palette.textPrimary : palette.inkOnBright
      let fill: Color =
        switch segment.state {
        case .done: palette.color(ColorTokens.setupDone)
        case .active: palette.primary
        case .pending: .clear
        }
      return HStack(spacing: Spacing.step3) {
        if let icon = segment.state.icon {
          CarbonIconShape(icon).fill(ink).frame(width: 20, height: 20)
        }
        segment.segment.label.text.role(Typography.statusLabel).foregroundStyle(ink)
        Spacer(minLength: 0)
      }
      .padding(.horizontal, Spacing.step4)
      .padding(.vertical, Spacing.step3)
      .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .leading)
      .background(fill)
      .overlay {
        if segment.state == .pending {
          Rectangle().strokeBorder(palette.borderStrong, lineWidth: 1)
        }
      }
    }
  }

  /// Outcome screens (UX-DR55): VoiceOver focus on the headline, one primary next action;
  /// success full-bleed `support-success`, an error with the "Step 5 stopped" eyebrow.
  struct OutcomeView: View {
    let outcome: OutcomePresentation
    let onAction: (OutcomeActionKind) -> Void
    var titleFocused: AccessibilityFocusState<Bool>.Binding
    @Environment(\.palette) private var palette

    var body: some View {
      let ink = outcome.isSuccess ? palette.inkOnBright : palette.textPrimary
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          if let eyebrow = outcome.eyebrow, let icon = outcome.eyebrowIcon {
            HStack(spacing: Spacing.step3) {
              CarbonIconShape(icon).fill(palette.supportErrorText).frame(width: 20, height: 20)
                .accessibilityHidden(true)
              Text(verbatim: eyebrow.string).role(Typography.statusLabel)
                .foregroundStyle(palette.supportErrorText)
            }
          }
          Text(verbatim: outcome.title.string).role(Typography.headline).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityAddTraits(.isHeader)
            .accessibilityFocused(titleFocused)
          Text(verbatim: outcome.body.string).role(Typography.bodyLg).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
          if let help = outcome.help {
            Text(verbatim: help.string).role(Typography.body).foregroundStyle(ink)
              .fixedSize(horizontal: false, vertical: true)
          }
          PrimaryButton(
            outcome.primary.label, variant: outcome.isSuccess ? .secondary : .primary
          ) { onAction(outcome.primary) }
          if let secondary = outcome.secondary {
            PrimaryButton(secondary.label, variant: .ghost) { onAction(secondary) }
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(
        (outcome.isSuccess ? palette.color(ColorTokens.supportSuccess) : palette.background)
          .ignoresSafeArea())
    }
  }

  /// A primary button whose label has arguments ("Set up Hub 3F2A").
  struct CopyButton: View {
    let copy: Copy
    let action: () -> Void
    @Environment(\.palette) private var palette

    init(_ copy: Copy, action: @escaping () -> Void) {
      self.copy = copy
      self.action = action
    }

    var body: some View {
      Button(action: action) {
        Text(verbatim: copy.string)
          .role(Typography.button)
          .multilineTextAlignment(.leading)
          .fixedSize(horizontal: false, vertical: true)
          .frame(maxWidth: .infinity, alignment: .leading)
          .padding(.horizontal, Spacing.step5)
          .padding(.vertical, Spacing.step4)
          .frame(minHeight: TouchTarget.control)
          .foregroundStyle(palette.inkOnBright)
          .background(palette.primary)
          .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
    }
  }
#endif
