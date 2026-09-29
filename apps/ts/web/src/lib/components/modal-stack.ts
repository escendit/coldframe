/** One modal level at a time (UX-DR76): the dialog that is open, if any. */
let openDialog: HTMLDialogElement | null = null;

/** Claims the single modal level; false when another dialog already holds it. */
export function claimModal(dialog: HTMLDialogElement): boolean {
  if (openDialog !== null && openDialog !== dialog && openDialog.open) {
    return false;
  }
  openDialog = dialog;
  return true;
}

/** Releases the modal level when the dialog closes. */
export function releaseModal(dialog: HTMLDialogElement): void {
  if (openDialog === dialog) {
    openDialog = null;
  }
}
