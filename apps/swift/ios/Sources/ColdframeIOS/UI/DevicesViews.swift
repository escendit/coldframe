#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Devices tab can ask of the core. Built over a `DevicesService`; `.none` does
  /// nothing, for previews and render tests.
  @MainActor
  public struct DevicesActions {
    public var load: () -> Void
    public var moveNode: (String, String) -> Void
    public var unassignNode: (String) -> Void
    /// The one-minute tick: the silence of silent Devices moves; nothing is fetched.
    public var tick: () -> Void
    /// The Device, what was typed and its unit; false when the core refused the value.
    public var setSilenceWindow: (String, String, SilenceWindowUnitKind) -> Bool
    public var useDefaultSilenceWindow: (String) -> Void

    public init(service: DevicesService) {
      load = { service.load() }
      moveNode = { service.moveNode($0, to: $1) }
      unassignNode = { service.unassignNode($0) }
      tick = { service.tick() }
      setSilenceWindow = { service.setSilenceWindow($0, text: $1, unit: $2) }
      useDefaultSilenceWindow = { service.useDefaultSilenceWindow($0) }
    }

    private init() {
      load = {}
      moveNode = { _, _ in }
      unassignNode = { _ in }
      tick = {}
      setSilenceWindow = { _, _, _ in true }
      useDefaultSilenceWindow = { _ in }
    }

    public static let none = DevicesActions()
  }

  /// The Devices tab (UX-DR30, UX-DR65): a "Hubs" section with one row per Hub, by Device ID,
  /// and Add a Hub and Add a Node as ghost header actions for Administrators and Owners (hidden
  /// for a Member, UX-DR84). Add a Node opens its flow with no Lot picked. A failed load shows
  /// the notice and no rows. `now` is the clock the last-seen times are told against.
  ///
  /// Silence (Story 7.2): a Device the Server says is silent shows the help icon and "Silent 6 h"
  /// with its last seen, and every row names its Silence Window. Changing it is a row action for
  /// Administrators and Owners only: for a Member the control does not exist. A one-minute tick
  /// moves the silences; it fetches nothing.
  public struct DevicesView: View {
    let presentation: DevicesPresentation
    let actions: DevicesActions
    let onAddHub: () -> Void
    let onAddNode: () -> Void
    let now: () -> Date
    let timeZone: TimeZone
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale
    /// The Node whose Move picker is open, and the Lot picked in it so far.
    @State private var moving: NodeRowPresentation?
    @State private var picked: String?
    /// The Node whose Unassign waits for its confirmation.
    @State private var unassigning: NodeRowPresentation?
    /// The Hub or Node whose Silence Window sheet is open.
    @State private var window: SilenceWindowPresentation?

    public init(
      presentation: DevicesPresentation, actions: DevicesActions = .none,
      onAddHub: @escaping () -> Void = {}, onAddNode: @escaping () -> Void = {},
      now: @escaping () -> Date = { Date() }, timeZone: TimeZone = .current
    ) {
      self.presentation = presentation
      self.actions = actions
      self.onAddHub = onAddHub
      self.onAddNode = onAddNode
      self.now = now
      self.timeZone = timeZone
    }

    public var body: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step5) {
          content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, Spacing.gutterMobile)
        .padding(.vertical, Spacing.step5)
      }
      .background(palette.background)
      .sheet(item: $moving) { node in
        moveSheet(node)
      }
      .sheet(item: $window) { device in
        SilenceWindowSheet(
          window: device,
          context: CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale),
          onSave: { text, unit in
            // The core says whether the value can be sent; a refused one keeps the sheet open.
            let accepted = actions.setSilenceWindow(device.id, text, unit)
            if accepted { window = nil }
            return accepted
          },
          onUseDefault: {
            actions.useDefaultSilenceWindow(device.id)
            window = nil
          },
          onCancel: { window = nil })
      }
      .task { [actions] in
        // The only timer of Devices: once a minute the core's snapshot is read again.
        while !Task.isCancelled {
          try? await Task.sleep(for: .seconds(60))
          if !Task.isCancelled {
            await MainActor.run { actions.tick() }
          }
        }
      }
      // Unassign confirms in a native dialog naming the Node, with the result as the verb.
      .confirmationDialog(
        Text(
          verbatim: unassigning.map { L10n.devicesUnassignQuestion.string($0.id) } ?? ""),
        isPresented: Binding(
          get: { unassigning != nil }, set: { if !$0 { unassigning = nil } }),
        titleVisibility: .visible,
        presenting: unassigning
      ) { node in
        Button(role: .destructive) {
          actions.unassignNode(node.id)
          unassigning = nil
        } label: {
          L10n.devicesUnassign.text
        }
        Button(role: .cancel) {
          unassigning = nil
        } label: {
          L10n.devicesCancel.text
        }
      } message: { node in
        Text(
          verbatim: L10n.devicesUnassignDetail.string(
            node.lotName ?? L10n.devicesNoLot.string))
      }
      .toolbar {
        if presentation.canAddHub {
          ToolbarItem(placement: .primaryAction) {
            PrimaryButton(.devicesAddHub, variant: .ghost, action: onAddHub)
          }
        }
        if presentation.canAddNode {
          ToolbarItem(placement: .primaryAction) {
            PrimaryButton(.devicesAddNode, variant: .ghost, action: onAddNode)
          }
        }
      }
    }

    @ViewBuilder
    private var content: some View {
      switch presentation.surface {
      case .waiting:
        // Nothing is shown while the list is read: an earlier answer's status is not kept.
        EmptyView()
      case .failed(let notice):
        InlineNotice(
          message: notice.message,
          action: notice.offersTryAgain ? (label: L10n.noticeTryAgain, perform: actions.load) : nil)
      case .ready(let hubs) where hubs.isEmpty && presentation.nodes.isEmpty:
        L10n.devicesEmpty.text.role(Typography.body).foregroundStyle(palette.textSecondary)
      case .ready(let hubs):
        if !hubs.isEmpty { hubsSection(hubs) }
        if !presentation.nodes.isEmpty { nodesSection(presentation.nodes) }
      }
    }

    private func hubsSection(_ hubs: [HubRowPresentation]) -> some View {
      let shownAt = now()
      return VStack(alignment: .leading, spacing: 0) {
        L10n.devicesHubs.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
          .padding(.bottom, Spacing.step4)
        divider
        ForEach(hubs) { hub in
          row(hub, shownAt: shownAt)
          divider
        }
      }
    }

    /// The "Nodes" section after the Hubs (UX-DR30), in the Server's order: Lot name, then
    /// unassigned last. Nothing here sorts.
    private func nodesSection(_ nodes: [NodeRowPresentation]) -> some View {
      let shownAt = now()
      return VStack(alignment: .leading, spacing: 0) {
        L10n.devicesNodes.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
          .padding(.bottom, Spacing.step4)
        divider
        ForEach(nodes) { node in
          nodeRow(node, shownAt: shownAt)
          divider
        }
      }
    }

    private var divider: some View {
      Rectangle().fill(palette.borderSubtle).frame(height: 1)
    }

    /// One element per row for VoiceOver: the ID, the status word, the silence, the last-seen
    /// time and the Silence Window. The action below it is also a custom accessibility action of
    /// that element.
    private func row(_ hub: HubRowPresentation, shownAt: Date) -> some View {
      let context = CopyContext.catalogue(now: shownAt, timeZone: timeZone, locale: locale)
      return VStack(alignment: .leading, spacing: Spacing.step2) {
        hubSummary(hub, shownAt: shownAt, context: context)
          .accessibilityElement(children: .combine)
          .modifier(
            SilenceWindowAccessibility(
              enabled: presentation.canSetSilenceWindow,
              label: L10n.devicesSilenceWindowHub.string(hub.id),
              onOpen: { openWindow(hub.id) }
            )
          )
        silenceWindowAction(
          hub.silence, label: L10n.devicesSilenceWindowHub.string(hub.id), deviceId: hub.id)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
      .padding(.vertical, Spacing.step4)
    }

    private func hubSummary(
      _ hub: HubRowPresentation, shownAt: Date, context: CopyContext
    ) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        // The full Device ID, never shortened or reformatted.
        Text(verbatim: hub.id).role(Typography.metaMono).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        HStack(spacing: Spacing.step2) {
          // The status is the word; the icon is its shape, never colour alone.
          CarbonIconShape(hub.icon).fill(palette.textPrimary).frame(width: 16, height: 16)
            .accessibilityHidden(true)
          hub.status.text.role(Typography.helper).foregroundStyle(palette.textPrimary)
        }
        silentMark(hub.silence, context)
        Text(
          verbatim: hub.lastSeenCopy(now: shownAt, timeZone: timeZone, locale: locale).string
        )
        .role(Typography.helper)
        .foregroundStyle(palette.textSecondary)
        .fixedSize(horizontal: false, vertical: true)
        windowSummary(hub.silence, context)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// A silent Device (Story 7.2): the `help` icon with "Silent 6 h", in sentence case in the
    /// catalogue and upper case by style. The icon is the shape, the word the meaning.
    @ViewBuilder
    private func silentMark(
      _ silence: DeviceSilencePresentation, _ context: CopyContext
    ) -> some View {
      if let text = silence.silentText(context), let icon = silence.icon {
        HStack(spacing: Spacing.step2) {
          CarbonIconShape(icon).fill(palette.textPrimary).frame(width: 16, height: 16)
            .accessibilityHidden(true)
          Text(verbatim: text).role(Typography.statusLabel)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }

    /// "Silence Window · 8 h": the window in force, for every Role. It is text, never a control.
    @ViewBuilder
    private func windowSummary(
      _ silence: DeviceSilencePresentation, _ context: CopyContext
    ) -> some View {
      if let text = silence.windowText(context) {
        Text(verbatim: text).role(Typography.helper).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
      }
    }

    /// The Silence Window row action, for Administrators and Owners only (UX-DR84): for a Member
    /// the control does not exist. Under it, why the last change did not happen; a 403 names
    /// the Site.
    @ViewBuilder
    private func silenceWindowAction(
      _ silence: DeviceSilencePresentation, label: String, deviceId: String
    ) -> some View {
      if presentation.canSetSilenceWindow {
        PrimaryButton(.devicesSilenceWindow, variant: .ghost, isEnabled: !silence.windowWorking) {
          openWindow(deviceId)
        }
        .accessibilityLabel(Text(verbatim: label))
      }
      if let notice = silence.windowNotice {
        InlineNotice(
          message: notice.message, subject: notice.takesSiteName ? presentation.siteName : nil,
          announcement: .assertive)
      }
    }

    private func openWindow(_ deviceId: String) {
      window = presentation.silenceWindow(for: deviceId)
    }

    /// One element per row for VoiceOver: the ID, the Lot, then last seen, battery and charging. The
    /// actions below it are also custom accessibility actions of that element (UX-DR31).
    private func nodeRow(_ node: NodeRowPresentation, shownAt: Date) -> some View {
      let unassignLabel = node.canUnassign ? L10n.devicesUnassignDescription.string(node.id) : nil
      let windowLabel = L10n.devicesSilenceWindowNode.string(node.id)
      return VStack(alignment: .leading, spacing: Spacing.step2) {
        nodeSummary(node, shownAt: shownAt)
          .accessibilityElement(children: .combine)
          .modifier(
            NodeActionsAccessibility(
              enabled: presentation.canManageNodes,
              moveLabel: L10n.devicesMoveDescription.string(node.id),
              unassignLabel: unassignLabel,
              onMove: { open(node) },
              onUnassign: { unassigning = node }
            )
          )
          .modifier(
            SilenceWindowAccessibility(
              enabled: presentation.canSetSilenceWindow, label: windowLabel,
              onOpen: { openWindow(node.id) }
            )
          )
        // Administrators and Owners only, hidden for a Member (UX-DR31); no Bluetooth is needed.
        if presentation.canManageNodes {
          HStack(spacing: Spacing.step4) {
            PrimaryButton(.devicesMove, variant: .ghost, isEnabled: !node.isWorking) {
              open(node)
            }
            .accessibilityLabel(Text(verbatim: L10n.devicesMoveDescription.string(node.id)))
            if node.canUnassign {
              PrimaryButton(.devicesUnassign, variant: .ghost, isEnabled: !node.isWorking) {
                unassigning = node
              }
              .accessibilityLabel(
                Text(verbatim: L10n.devicesUnassignDescription.string(node.id)))
            }
          }
        }
        if let notice = node.actionNotice {
          InlineNotice(message: notice.message, announcement: .assertive)
        }
        silenceWindowAction(node.silence, label: windowLabel, deviceId: node.id)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
      .padding(.vertical, Spacing.step4)
    }

    private func open(_ node: NodeRowPresentation) {
      picked = nil
      moving = node
    }

    private func nodeSummary(_ node: NodeRowPresentation, shownAt: Date) -> some View {
      let context = CopyContext.catalogue(now: shownAt, timeZone: timeZone, locale: locale)
      return VStack(alignment: .leading, spacing: Spacing.step2) {
        Text(verbatim: node.id).role(Typography.metaMono).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        Group {
          if let lotName = node.lotName {
            Text(verbatim: lotName)
          } else {
            L10n.devicesNoLot.text
          }
        }
        .role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
        .fixedSize(horizontal: false, vertical: true)
        silentMark(node.silence, context)
        HStack(spacing: Spacing.step5) {
          Text(
            verbatim: node.lastSeenCopy(now: shownAt, timeZone: timeZone, locale: locale).string
          )
          .role(Typography.helper).foregroundStyle(palette.textSecondary)
          if let battery = node.batteryCopy {
            HStack(spacing: Spacing.step2) {
              if let icon = node.batteryIcon {
                CarbonIconShape(icon).fill(palette.textPrimary).frame(width: 16, height: 16)
                  .accessibilityHidden(true)
              }
              Text(verbatim: battery.string).role(Typography.helper)
                .foregroundStyle(palette.textSecondary)
            }
          }
          if let charging = node.chargingLabel {
            charging.text.role(Typography.helper).foregroundStyle(palette.textSecondary)
          }
        }
        .fixedSize(horizontal: false, vertical: true)
        windowSummary(node.silence, context)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// Move a Node (UX-DR31): the Lots in the Server's order. A Lot that has another Node is
    /// disabled with "Has a Node", in words and not by colour alone, and the Node's own Lot says so.
    private func moveSheet(_ node: NodeRowPresentation) -> some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step5) {
          Text(verbatim: L10n.devicesMoveChoose.string(node.id)).role(Typography.section)
            .foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
            .fixedSize(horizontal: false, vertical: true)
          let choices = presentation.moveChoices(for: node, selected: picked)
          if choices.isEmpty {
            L10n.devicesMoveNoLots.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          }
          VStack(spacing: Spacing.tileGap) {
            ForEach(choices) { lot in
              MoveLotRowView(lot: lot) { picked = lot.id }
            }
          }
          HStack(spacing: Spacing.step4) {
            PrimaryButton(.devicesCancel, variant: .ghost) { moving = nil }
            PrimaryButton(.devicesMoveConfirm, isEnabled: picked != nil) {
              if let lotId = picked {
                actions.moveNode(node.id, lotId)
              }
              moving = nil
            }
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
    }
  }

  /// Marks the actions of a Node row as custom VoiceOver actions, for a Role that may use them.
  private struct NodeActionsAccessibility: ViewModifier {
    let enabled: Bool
    let moveLabel: String
    let unassignLabel: String?
    let onMove: () -> Void
    let onUnassign: () -> Void

    func body(content: Content) -> some View {
      if enabled {
        if let unassignLabel {
          content
            .accessibilityAction(named: Text(verbatim: moveLabel), onMove)
            .accessibilityAction(named: Text(verbatim: unassignLabel), onUnassign)
        } else {
          content.accessibilityAction(named: Text(verbatim: moveLabel), onMove)
        }
      } else {
        content
      }
    }
  }

  /// Marks the Silence Window action of a Device row as a custom VoiceOver action, for a Role that
  /// may use it.
  private struct SilenceWindowAccessibility: ViewModifier {
    let enabled: Bool
    let label: String
    let onOpen: () -> Void

    func body(content: Content) -> some View {
      if enabled {
        content.accessibilityAction(named: Text(verbatim: label), onOpen)
      } else {
        content
      }
    }
  }

  /// Silence Window (UX-DR31, UX-DR84, Story 7.2): the window in force, then a number with a unit
  /// of min, h or d. Save hands what was typed to the core, which refuses a value that is no
  /// whole number from 1 min to 30 days and sends nothing; the range then stands under the field.
  /// "Use default" shows only when the Device has its own window.
  struct SilenceWindowSheet: View {
    let window: SilenceWindowPresentation
    let context: CopyContext
    /// What was typed and its unit; false when the core refused the value.
    let onSave: (String, SilenceWindowUnitKind) -> Bool
    let onUseDefault: () -> Void
    let onCancel: () -> Void
    @State private var text: String
    @State private var unit: SilenceWindowUnitKind
    @State private var refused = false
    @Environment(\.palette) private var palette

    init(
      window: SilenceWindowPresentation, context: CopyContext,
      onSave: @escaping (String, SilenceWindowUnitKind) -> Bool,
      onUseDefault: @escaping () -> Void = {}, onCancel: @escaping () -> Void = {}
    ) {
      self.window = window
      self.context = context
      self.onSave = onSave
      self.onUseDefault = onUseDefault
      self.onCancel = onCancel
      _text = State(initialValue: window.initialText)
      _unit = State(initialValue: window.initialUnit)
    }

    var body: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step5) {
          Text(verbatim: context.resolve(window.title)).role(Typography.section)
            .foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
            .fixedSize(horizontal: false, vertical: true)
          if let current = window.currentText(context) {
            Text(verbatim: current).role(Typography.helper)
              .foregroundStyle(palette.textSecondary)
              .fixedSize(horizontal: false, vertical: true)
          }
          TextInputField(
            label: SilenceWindowPresentation.label.string, value: $text,
            helper: SilenceWindowPresentation.helper.string,
            error: refused ? SilenceWindowPresentation.rangeError.string : nil
          )
          #if os(iOS)
            .keyboardType(.numberPad)
          #endif
          SegmentedChoice(
            label: SilenceWindowPresentation.unitLabel,
            segments: window.unitSegments(selected: unit)
          ) { picked in
            unit = picked
            refused = false
          }
          HStack(spacing: Spacing.step4) {
            PrimaryButton(.devicesCancel, variant: .ghost, action: onCancel)
            PrimaryButton(.devicesSilenceWindowSave) {
              refused = !onSave(text, unit)
            }
          }
          if window.showsUseDefault {
            PrimaryButton(.devicesSilenceWindowUseDefault, variant: .ghost, action: onUseDefault)
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      // A new entry is a new try: the refusal belongs to the value that was refused.
      .onChange(of: text) { refused = false }
    }
  }

  /// One Lot of the Move picker (UX-DR31), in the Lot picker's look: the selected state is a 2 pt
  /// `primary-text` border plus a checkmark. A Lot that cannot be picked is dimmed and its reason is
  /// a word. One element that reads "Beans, has a Node", or the name alone.
  private struct MoveLotRowView: View {
    let lot: MoveLotPresentation
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
#endif
