#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What Add a Node can ask of the core; the views forward taps and render what comes back.
  /// `open` takes the Lot of a *no Node* tile, or nil from Devices.
  @MainActor
  public struct NodeSetupActions {
    public var open: (String?) -> Void
    public var close: () -> Void
    public var recheckRadio: () -> Void
    public var announcing: (Bool) -> Void
    public var back: () -> Void
    public var leave: () -> Void
    public var confirmLeave: () -> Void
    public var stayInFlow: () -> Void
    public var continueFromPress: () -> Void
    public var select: (String) -> Void
    public var continueFromScan: () -> Void
    public var setCode: (String) -> Void
    public var submitCode: () -> Void
    public var continueFromCode: () -> Void
    public var retryLots: () -> Void
    public var chooseLot: (String) -> Void
    public var openNewLot: () -> Void
    public var setNewLotName: (String) -> Void
    public var createLot: () -> Void
    public var assign: () -> Void
    public var outcomeAction: (NodeOutcomeActionKind) -> Void

    public init(service: NodeSetupService) {
      open = { service.open(lotId: $0) }
      close = { service.close() }
      recheckRadio = { service.recheckRadio() }
      announcing = { service.announcing($0) }
      back = { service.back() }
      leave = { service.leave() }
      confirmLeave = { service.confirmLeave() }
      stayInFlow = { service.stayInFlow() }
      continueFromPress = { service.continueFromPress() }
      select = { service.select(candidateId: $0) }
      continueFromScan = { service.continueFromScan() }
      setCode = { service.setCode($0) }
      submitCode = { service.submitCode() }
      continueFromCode = { service.continueFromCode() }
      retryLots = { service.retryLots() }
      chooseLot = { service.chooseLot($0) }
      openNewLot = { service.openNewLot() }
      setNewLotName = { service.setNewLotName($0) }
      createLot = { service.createLot() }
      assign = { service.assign() }
      outcomeAction = { service.outcomeAction($0) }
    }

    private init() {
      open = { _ in }
      close = {}
      recheckRadio = {}
      announcing = { _ in }
      back = {}
      leave = {}
      confirmLeave = {}
      stayInFlow = {}
      continueFromPress = {}
      select = { _ in }
      continueFromScan = {}
      setCode = { _ in }
      submitCode = {}
      continueFromCode = {}
      retryLots = {}
      chooseLot = { _ in }
      openNewLot = {}
      setNewLotName = { _ in }
      createLot = {}
      assign = {}
      outcomeAction = { _ in }
    }

    public static let none = NodeSetupActions()
  }

  /// Add a Node (UX-DR67): press the setup button, pick the Node, its setup code, the Lot
  /// picker, then the outcome, full screen over the tab shell in the same Setup flow shell as
  /// Add a Hub. Top-left is Cancel on step 1 and Back afterwards; both ask the core, which
  /// closes, goes one step back, or asks before it leaves once a Lot is being picked. VoiceOver
  /// focus moves to the step title or headline; the idle timer, the leave question and the
  /// announcements (UX-DR105) are `SetupFlowEffects`, shared with Add a Hub.
  public struct AddNodeFlowView: View {
    let presentation: NodeSetupPresentation
    let actions: NodeSetupActions
    @AccessibilityFocusState private var titleFocused: Bool

    public init(presentation: NodeSetupPresentation, actions: NodeSetupActions) {
      self.presentation = presentation
      self.actions = actions
    }

    public var body: some View {
      Group {
        if let outcome = presentation.outcome {
          OutcomeView(
            isSuccess: outcome.isSuccess, eyebrow: outcome.eyebrow?.string,
            title: outcome.title.string, detail: outcome.body.string, help: nil,
            primary: outcome.primary.label, onPrimary: { actions.outcomeAction(outcome.primary) },
            secondary: nil, onSecondary: {}, titleFocused: $titleFocused)
        } else {
          SetupFlowShellView(
            backLabel: presentation.backLabel, onBack: actions.back,
            counterCurrent: presentation.counterCurrent,
            counterTotal: presentation.counterTotal.string,
            counterDescription: presentation.counterDescription.string,
            title: presentation.title.string, titleFocused: $titleFocused
          ) {
            step
          }
        }
      }
      .onAppear { titleFocused = true }
      .onChange(of: presentation.step) { titleFocused = true }
      .onChange(of: presentation.outcome?.kind) { titleFocused = true }
      .setupFlowEffects(
        keepAwake: presentation.keepAwake, onRecheckRadio: actions.recheckRadio,
        leaveQuestion: presentation.leaveQuestion.string, leaveDetail: presentation.leaveDetail,
        confirmingLeave: presentation.confirmingLeave, onConfirmLeave: actions.confirmLeave,
        onStay: actions.stayInFlow, announcementId: presentation.announcement?.id,
        isAssertive: presentation.announcement?.isAssertive == true,
        announcementText: { announcementText() }, onAnnouncing: actions.announcing)
    }

    @ViewBuilder
    private var step: some View {
      switch presentation.step {
      case .press: NodePressStepView(actions: actions)
      case .scan: NodeScanStepView(presentation: presentation, actions: actions)
      case .code: NodeCodeStepView(presentation: presentation, actions: actions)
      case .lot, .outcome: NodeLotStepView(presentation: presentation, actions: actions)
      }
    }

    private func announcementText() -> String? {
      presentation.spokenAnnouncement { $0.string }
    }
  }

  /// Step 1: how to wake the Node, then "Look for the Node".
  struct NodePressStepView: View {
    let actions: NodeSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.addNodePressBody.text.role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        PrimaryButton(.addNodePressAction, action: actions.continueFromPress)
      }
    }
  }

  /// Step 2: candidate tiles strongest first, the Bluetooth and no-Node notices, "Still
  /// scanning…" and the primary action naming the Node.
  struct NodeScanStepView: View {
    let presentation: NodeSetupPresentation
    let actions: NodeSetupActions
    @Environment(\.palette) private var palette
    @Environment(\.openURL) private var openURL

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.addNodeScanIntro.text.role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        if let notice = presentation.radio.nodeNotice {
          InlineNotice(
            message: notice, announcement: .assertive,
            action: presentation.radio.opensSettings
              ? (.addNodeOpenSettings, { openSettings() }) : nil)
        }
        if presentation.noNodeYet {
          InlineNotice(message: .addNodeNoNode)
        }
        VStack(spacing: Spacing.tileGap) {
          ForEach(presentation.candidates) { candidate in
            let signal = candidate.signal.label.string
            CandidateTileView(
              name: candidate.name.string, signal: signal, badge: candidate.badge?.string,
              description: candidate.description(signal: signal).string,
              isSelected: candidate.isSelected
            ) { actions.select(candidate.id) }
          }
        }
        if presentation.showsStillScanning {
          L10n.addNodeStillScanning.text.role(Typography.body)
            .foregroundStyle(palette.textSecondary)
        }
        if let action = presentation.scanAction {
          CopyButton(action, action: actions.continueFromScan)
        }
      }
    }

    private func openSettings() { openAppSettings(with: openURL) }
  }

  /// Step 3: the setup code field (UX-DR41) with its "Accepted" chip, then the Device ID. The
  /// chip shows once the core holds the Node's sealed key and has ended the connection.
  struct NodeCodeStepView: View {
    let presentation: NodeSetupPresentation
    let actions: NodeSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.addNodeCodeIntro.text.role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        SetupCodeFieldView(
          label: L10n.addNodeCodeLabel.string, text: presentation.codeText,
          helper: L10n.addNodeCodeHelper.string, acceptedLabel: L10n.addNodeCodeAccepted.string,
          isAccepted: presentation.codeAccepted, error: presentation.codeErrorMessage?.string,
          onChange: actions.setCode)
        if let deviceId = presentation.deviceId {
          VStack(alignment: .leading, spacing: Spacing.step2) {
            L10n.addNodeDeviceId.text.role(Typography.helper).foregroundStyle(palette.textHelper)
            Text(verbatim: deviceId).role(Typography.metaMono).foregroundStyle(palette.textPrimary)
          }
        }
        PrimaryButton(
          presentation.codeAction, isEnabled: presentation.isCodeActionEnabled,
          action: presentation.codeAccepted ? actions.continueFromCode : actions.submitCode)
      }
    }
  }

  /// Step 4, the Lot picker (UX-DR38): the Lots of the Site in the Server's order, a Lot that
  /// has a Node disabled with its reason, "+ New Lot" opening the inline name field of Create
  /// Lot, the notice with Try again where reading again can help, and the primary action naming
  /// the Node and the Lot. Needs no Bluetooth: the Node's sealed key is already held.
  struct NodeLotStepView: View {
    let presentation: NodeSetupPresentation
    let actions: NodeSetupActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        Text(verbatim: presentation.lotIntro.string).role(Typography.body)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        if let notice = presentation.lotNotice {
          InlineNotice(
            message: notice.message, subject: presentation.lotNoticeLot, announcement: .assertive,
            action: presentation.offersLotsRetry ? (.noticeTryAgain, actions.retryLots) : nil)
        }
        if let lots = presentation.lots {
          VStack(spacing: Spacing.tileGap) {
            ForEach(lots) { lot in
              LotPickerRowView(lot: lot) { actions.chooseLot(lot.id) }
            }
            if presentation.showsNewLotTile {
              NewLotTileView(onOpen: actions.openNewLot)
            }
          }
          if let newLot = presentation.newLot {
            TextInputField(
              label: newLot.label.string,
              value: Binding(get: { newLot.name }, set: { actions.setNewLotName($0) }),
              helper: newLot.helper.string, error: newLot.errorMessage?.string)
            PrimaryButton(
              newLot.buttonLabel, isEnabled: newLot.isButtonEnabled, action: actions.createLot)
          }
          if let action = presentation.assignAction {
            CopyButton(action, isEnabled: presentation.isAssignEnabled, action: actions.assign)
          }
        } else if let loading = presentation.lotsLoading {
          Text(verbatim: loading.string).role(Typography.body)
            .foregroundStyle(palette.textSecondary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }
  }

  /// One Lot of the Lot picker (UX-DR38): the Device candidate's selected state (2 pt
  /// `primary-text` border plus a checkmark, exposed as selected). A Lot that has a Node is
  /// disabled: its name is dimmed and "Has a Node" says why, in words and not by colour alone.
  /// One element that reads "Beans, has a Node", or the name alone.
  struct LotPickerRowView: View {
    let lot: LotChoicePresentation
    let onSelect: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Button(action: onSelect) {
        HStack(spacing: Spacing.step4) {
          VStack(alignment: .leading, spacing: Spacing.step2) {
            Text(verbatim: lot.name).role(Typography.tileName)
              .foregroundStyle(lot.isSelectable ? palette.textPrimary : palette.textSecondary)
              .multilineTextAlignment(.leading)
              .fixedSize(horizontal: false, vertical: true)
            if let reason = lot.reason {
              reason.text.role(Typography.statusLabel).foregroundStyle(palette.textSecondary)
            }
          }
          Spacer(minLength: 0)
          if lot.isSelected {
            CarbonIconShape(.checkmark).fill(palette.primaryText).frame(width: 20, height: 20)
              .accessibilityHidden(true)
          }
        }
        .padding(Spacing.tilePadding)
        .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .leading)
        .background(lot.isSelectable ? palette.layer01 : Color.clear)
        .overlay {
          if !lot.isSelectable {
            Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1)
          } else if lot.isSelected {
            Rectangle().strokeBorder(palette.primaryText, lineWidth: 2)
          }
        }
        .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
      .disabled(!lot.isSelectable)
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: lot.description?.string ?? lot.name))
      .accessibilityAddTraits(lot.isSelected ? [.isButton, .isSelected] : .isButton)
    }
  }

  /// "+ New Lot" (UX-DR38): a dotted tile that opens the inline name field.
  struct NewLotTileView: View {
    let onOpen: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Button(action: onOpen) {
        L10n.addNodeNewLot.text.role(Typography.tileName).foregroundStyle(palette.textPrimary)
          .padding(Spacing.tilePadding)
          .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .leading)
          .overlay {
            Rectangle().strokeBorder(
              palette.color(ColorTokens.statusNoNodeBorder),
              style: StrokeStyle(lineWidth: 1, lineCap: .round, dash: [1, 3]))
          }
          .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
    }
  }
#endif
