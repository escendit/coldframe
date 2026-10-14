#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What Members can ask of the core. Built over a `MembersService`; `.none` does nothing, for
  /// previews and render tests.
  @MainActor
  public struct MembersActions {
    /// Every entry of the surface reads the Memberships again.
    public var load: () -> Void
    public var setEmail: (String) -> Void
    public var setRole: (SiteRoleKind) -> Void
    public var sendInvite: () -> Void
    /// Try again, where the notice offers it.
    public var retry: () -> Void

    public init(service: MembersService) {
      load = { service.load() }
      setEmail = { service.setEmail($0) }
      setRole = { service.setRole($0) }
      sendInvite = { service.sendInvite() }
      retry = { service.retry() }
    }

    private init() {
      load = {}
      setEmail = { _ in }
      setRole = { _ in }
      sendInvite = {}
      retry = {}
    }

    public static let none = MembersActions()
  }

  /// Members (Story 9.1, UX-DR73): one Member tile per Membership (UX-DR51), then the Invite form
  /// (UX-DR52): the email field with the email keyboard, the Role Segmented choice with its
  /// one-line description, and "Send invite". Owner only; there is no pending-invitation list.
  /// Every entry reads the Memberships again.
  public struct MembersView: View {
    let presentation: MembersPresentation
    let actions: MembersActions
    @Environment(\.palette) private var palette

    public init(presentation: MembersPresentation, actions: MembersActions) {
      self.presentation = presentation
      self.actions = actions
    }

    public var body: some View {
      ScrollView {
        Group {
          switch presentation.surface {
          case .hidden, .loading:
            EmptyView()
          case .failed:
            notice
          case .ready:
            content
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .navigationTitle(L10n.membersTitle.string)
      .onAppear { actions.load() }
    }

    @ViewBuilder
    private var notice: some View {
      if let notice = presentation.notice, let copy = presentation.noticeCopy {
        InlineNotice(
          text: copy.string, announcement: notice.announcement)
        if presentation.noticeTryAgain {
          PrimaryButton(.noticeTryAgain, variant: .secondary, action: actions.retry)
        }
      }
    }

    private var content: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        VStack(alignment: .leading, spacing: Spacing.step3) {
          // The Server's order, never re-sorted.
          ForEach(presentation.members) { MemberTileView(presentation: $0) }
        }
        L10n.membersInvite.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        TextInputField(
          label: L10n.membersEmail.string,
          value: Binding(get: { presentation.email }, set: { actions.setEmail($0) }),
          helper: presentation.emailError == nil ? L10n.membersEmailHelper.string : nil,
          error: presentation.emailError?.message.string, kind: .email)
        SegmentedChoice(
          label: .membersRole, segments: presentation.roleSegments,
          helper: presentation.roleDescription, onSelect: actions.setRole)
        notice
        PrimaryButton(
          presentation.buttonLabel, isEnabled: presentation.isButtonEnabled,
          action: actions.sendInvite)
      }
    }
  }

  /// The Member tile (UX-DR51): the initials square (the User's own `primary` with
  /// `ink-on-bright`, the others `layer-01` with `text-primary`), the name and the Role line in
  /// `status-label` (upper case by style). One accessibility element: "Simon Novak, Owner, you".
  struct MemberTileView: View {
    let presentation: MemberTilePresentation
    @Environment(\.palette) private var palette

    var body: some View {
      HStack(spacing: Spacing.step4) {
        Text(verbatim: presentation.initials).role(Typography.section)
          .foregroundStyle(presentation.isYou ? palette.inkOnBright : palette.textPrimary)
          .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
          .background(presentation.isYou ? palette.primary : palette.layer01)
        VStack(alignment: .leading, spacing: Spacing.step1) {
          Text(verbatim: presentation.name).role(Typography.bodyLg)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
          presentation.roleLabel.text.role(Typography.statusLabel)
            .foregroundStyle(palette.textSecondary)
        }
        Spacer(minLength: 0)
      }
      .padding(Spacing.step4)
      .frame(maxWidth: .infinity, minHeight: TouchTarget.minimum, alignment: .leading)
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: presentation.spoken(.catalogue())))
    }
  }
#endif
