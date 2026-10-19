import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosMembers` of the shared Kotlin core to `MembersService` (Stories 9.1, 9.2). The snapshot
/// is flat strings and flags; the Server calls, the email check and what a refused invitation
/// leaves stay in the core (AD-14).
@MainActor
final class CoreMembersService: MembersService {
  private let core: IosMembers
  private var watch: Watch?

  init(core: IosMembers) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (MembersPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          MembersPresentation(
            surface: snapshot.surface, siteName: snapshot.siteName,
            memberIds: snapshot.memberIds, memberNames: snapshot.memberNames,
            memberEmails: snapshot.memberEmails, memberRoles: snapshot.memberRoles,
            memberInitials: snapshot.memberInitials,
            memberIsYou: snapshot.memberIsYou.map { $0.boolValue }, email: snapshot.email,
            role: snapshot.role, roles: snapshot.roles, emailError: snapshot.emailError,
            working: snapshot.working, notice: snapshot.notice,
            noticeTryAgain: snapshot.noticeTryAgain, sentTo: snapshot.sentTo,
            memberLastOwner: snapshot.memberLastOwner.map { $0.boolValue },
            editorUserId: snapshot.editorUserId, editorName: snapshot.editorName,
            editorRole: snapshot.editorRole, editorRoleError: snapshot.editorRoleError,
            editorWorking: snapshot.editorWorking, confirmingRemove: snapshot.confirmingRemove,
            noticeName: snapshot.noticeName, noticeRole: snapshot.noticeRole))
      }
    }
  }

  func load() { core.load() }

  func setEmail(_ email: String) { core.setEmail(email: email) }

  /// The core takes the catalogue key suffix: `member`, `administrator`, `owner`.
  func setRole(_ role: SiteRoleKind) { core.setRole(role: role.rawValue) }

  func sendInvite() { core.sendInvite() }

  func retry() { core.retry() }

  // Story 9.2: the Role editor and the removal; the last-Owner rule stays in the core and Server.

  func select(_ userId: String) { core.select(userId: userId) }

  func closeEditor() { core.closeEditor() }

  func setEditRole(_ role: SiteRoleKind) { core.setEditRole(role: role.rawValue) }

  func saveRole() { core.saveRole() }

  func askRemove() { core.askRemove() }

  func cancelRemove() { core.cancelRemove() }

  func confirmRemove() { core.confirmRemove() }
}
