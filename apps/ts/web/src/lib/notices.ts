import type { MessageKey } from '$lib/i18n';

/** Inline notices of the Sign-in card (UX-DR92, UX-DR93). */
export type Notice = 'unreachable' | 'certificate' | 'keycloak' | 'signed-out';

export const notices: readonly Notice[] = ['unreachable', 'certificate', 'keycloak', 'signed-out'];

export interface NoticeCopy {
  readonly message: MessageKey;
  /** The one action, or null: the certificate notice deliberately has none (AD-13). */
  readonly action: MessageKey | null;
  /** Errors are announced assertively; the signed-out note politely. */
  readonly priority: 'polite' | 'assertive';
}

export const noticeCopy: Readonly<Record<Notice, NoticeCopy>> = {
  unreachable: { message: 'notice.unreachable', action: 'notice.tryAgain', priority: 'assertive' },
  certificate: { message: 'notice.certificate', action: null, priority: 'assertive' },
  keycloak: { message: 'notice.keycloak', action: 'notice.tryAgain', priority: 'assertive' },
  'signed-out': { message: 'notice.signedOut', action: 'notice.signIn', priority: 'polite' },
};

/** Parses `?notice=`; unknown values show no notice. */
export function parseNotice(value: string | null | undefined): Notice | null {
  return notices.find((notice) => notice === value) ?? null;
}
