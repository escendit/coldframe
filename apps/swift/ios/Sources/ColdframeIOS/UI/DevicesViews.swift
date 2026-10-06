#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Devices tab can ask of the core. Built over a `DevicesService`; `.none` does
  /// nothing, for previews and render tests.
  @MainActor
  public struct DevicesActions {
    public var load: () -> Void

    public init(service: DevicesService) {
      load = { service.load() }
    }

    private init() {
      load = {}
    }

    public static let none = DevicesActions()
  }

  /// The Devices tab (UX-DR30, UX-DR65): a "Hubs" section with one row per Hub, by Device ID,
  /// and Add a Hub and Add a Node as ghost header actions for Administrators and Owners (hidden
  /// for a Member, UX-DR84). Add a Node opens its flow with no Lot picked. A failed load shows
  /// the notice and no rows. `now` is the clock the last-seen times are told against.
  public struct DevicesView: View {
    let presentation: DevicesPresentation
    let actions: DevicesActions
    let onAddHub: () -> Void
    let onAddNode: () -> Void
    let now: () -> Date
    let timeZone: TimeZone
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

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
      case .ready(let hubs) where hubs.isEmpty:
        L10n.devicesEmpty.text.role(Typography.body).foregroundStyle(palette.textSecondary)
      case .ready(let hubs):
        hubsSection(hubs)
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

    private var divider: some View {
      Rectangle().fill(palette.borderSubtle).frame(height: 1)
    }

    /// One element per row for VoiceOver: the ID, the status word, then the last-seen time.
    private func row(_ hub: HubRowPresentation, shownAt: Date) -> some View {
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
        Text(
          verbatim: hub.lastSeenCopy(now: shownAt, timeZone: timeZone, locale: locale).string
        )
        .role(Typography.helper)
        .foregroundStyle(palette.textSecondary)
        .fixedSize(horizontal: false, vertical: true)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
      .padding(.vertical, Spacing.step4)
      .accessibilityElement(children: .combine)
    }
  }
#endif
