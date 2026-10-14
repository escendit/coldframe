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
    /// Story 9.2: the Role editor and the removal.
    public var select: (String) -> Void
    public var closeEditor: () -> Void
    public var setEditRole: (SiteRoleKind) -> Void
    public var saveRole: () -> Void
    public var askRemove: () -> Void
    public var cancelRemove: () -> Void
    public var confirmRemove: () -> Void

    public init(service: MembersService) {
      load = { service.load() }
      setEmail = { service.setEmail($0) }
      setRole = { service.setRole($0) }
      sendInvite = { service.sendInvite() }
      retry = { service.retry() }
      select = { service.select($0) }
      closeEditor = { service.closeEditor() }
      setEditRole = { service.setEditRole($0) }
      saveRole = { service.saveRole() }
      askRemove = { service.askRemove() }
      cancelRemove = { service.cancelRemove() }
      confirmRemove = { service.confirmRemove() }
    }

    private init() {
      load = {}
      setEmail = { _ in }
      setRole = { _ in }
      sendInvite = {}
      retry = {}
      select = { _ in }
      closeEditor = {}
      setEditRole = { _ in }
      saveRole = {}
      askRemove = {}
      cancelRemove = {}
      confirmRemove = {}
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
      .sheet(
        item: Binding(
          get: { presentation.editor },
          set: { if $0 == nil { actions.closeEditor() } })
      ) { editor in
        NavigationStack {
          MemberEditorView(editor: editor, actions: actions) { notice }
        }
        .interactiveDismissDisabled(editor.working)
      }
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
          ForEach(presentation.members) { tile in
            MemberTileView(presentation: tile) { actions.select(tile.id) }
          }
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
        // While the editor is open, its notice shows there.
        if presentation.editor == nil {
          notice
        }
        PrimaryButton(
          presentation.buttonLabel, isEnabled: presentation.isButtonEnabled,
          action: actions.sendInvite)
      }
    }
  }

  /// The Member tile (UX-DR51): the initials square (the User's own `primary` with
  /// `ink-on-bright`, the others `layer-01` with `text-primary`), the name and the Role line in
  /// `status-label` (upper case by style). One accessibility element: "Simon Novak, Owner, you".
  /// Story 9.2: a button that opens the Role editor ("Member · change"); the only Owner's tile is
  /// locked ("Owner · last one" and why) and is no button.
  struct MemberTileView: View {
    let presentation: MemberTilePresentation
    let onSelect: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      if presentation.isChangeable {
        Button(action: onSelect) { tile }
          .buttonStyle(.plain)
          .accessibilityElement(children: .ignore)
          .accessibilityLabel(Text(verbatim: presentation.spoken(.catalogue())))
          .accessibilityAddTraits(.isButton)
      } else {
        tile
          .accessibilityElement(children: .ignore)
          .accessibilityLabel(Text(verbatim: presentation.spoken(.catalogue())))
          .accessibilityHint(presentation.lockExplanation?.text ?? Text(verbatim: ""))
      }
    }

    private var tile: some View {
      HStack(spacing: Spacing.step4) {
        Text(verbatim: presentation.initials).role(Typography.section)
          .foregroundStyle(presentation.isYou ? palette.inkOnBright : palette.textPrimary)
          .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
          .background(presentation.isYou ? palette.primary : palette.layer01)
        VStack(alignment: .leading, spacing: Spacing.step1) {
          Text(verbatim: presentation.name).role(Typography.bodyLg)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
          Text(verbatim: presentation.roleLine.string).role(Typography.statusLabel)
            .foregroundStyle(palette.textSecondary)
          if let lock = presentation.lockExplanation {
            lock.text.role(Typography.helper).foregroundStyle(palette.textHelper)
              .fixedSize(horizontal: false, vertical: true)
          }
        }
        Spacer(minLength: 0)
      }
      .padding(Spacing.step4)
      .frame(maxWidth: .infinity, minHeight: TouchTarget.minimum, alignment: .leading)
      .contentShape(Rectangle())
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
    }
  }

  /// The Role editor of one Membership (Story 9.2): the Role Segmented choice with one line under
  /// it when the Server refused the last Owner, "Save Role", and "Remove from Site", which asks in
  /// a native confirmation dialog naming the person and the Site.
  struct MemberEditorView<Notice: View>: View {
    let editor: MemberEditorPresentation
    let actions: MembersActions
    @ViewBuilder let notice: () -> Notice
    @Environment(\.palette) private var palette

    var body: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          SegmentedChoice(
            label: .membersRole, segments: editor.roleSegments, helper: editor.roleLine,
            onSelect: actions.setEditRole)
          notice()
          PrimaryButton(
            editor.saveLabel, isEnabled: editor.areButtonsEnabled, action: actions.saveRole)
          PrimaryButton(
            editor.removeLabel, variant: .secondary, isEnabled: editor.areButtonsEnabled,
            action: actions.askRemove)
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .navigationTitle(Text(verbatim: editor.name))
      .toolbar {
        ToolbarItem(placement: .cancellationAction) {
          Button(action: actions.closeEditor) { L10n.membersCancel.text }
        }
      }
      .confirmationDialog(
        Text(verbatim: editor.removal?.title.string ?? ""),
        isPresented: Binding(
          get: { editor.removal != nil },
          set: { if !$0 { actions.cancelRemove() } }),
        titleVisibility: .visible,
        presenting: editor.removal
      ) { removal in
        Button(role: .destructive, action: actions.confirmRemove) { removal.confirm.text }
          .disabled(removal.working)
        Button(role: .cancel, action: actions.cancelRemove) { removal.cancel.text }
      } message: { removal in
        Text(verbatim: removal.message.string)
      }
    }
  }
#endif
