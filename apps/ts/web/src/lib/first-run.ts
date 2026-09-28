import type { MessageKey } from '$lib/i18n';
import { hasRole, type Role } from '$lib/roles';

/** The four first-run steps, in order (UX-DR54). */
export type FirstRunStep = 'addHub' | 'addNode' | 'calibrate' | 'setThreshold';

export type StepState = 'next' | 'later' | 'done';

export interface StepTile {
  readonly step: FirstRunStep;
  readonly number: number;
  readonly label: MessageKey;
  readonly state: StepState;
}

export interface FirstRunSteps {
  readonly tiles: readonly StepTile[];
  /** The next step starts its flow. Only on mobile, for Administrator and Owner, once the flow exists. */
  readonly actionable: boolean;
  /** Members see the read-only notice. */
  readonly memberNotice: boolean;
}

const order: readonly [FirstRunStep, MessageKey][] = [
  ['addHub', 'garden.step.addHub'],
  ['addNode', 'garden.step.addNode'],
  ['calibrate', 'garden.step.calibrate'],
  ['setThreshold', 'garden.step.setThreshold'],
];

/**
 * The tiles of an empty Site. Nothing is done yet in Story 1.8, so Add a Hub is next and the rest
 * are later. The web never starts a BLE flow, so it always passes `flowAvailable = false`.
 */
export function firstRunSteps(role: Role, flowAvailable = false): FirstRunSteps {
  return {
    tiles: order.map(([step, label], index) => ({ step, number: index + 1, label, state: index === 0 ? 'next' : 'later' })),
    actionable: flowAvailable && hasRole(role, 'Administrator'),
    memberNotice: role === 'Member',
  };
}
