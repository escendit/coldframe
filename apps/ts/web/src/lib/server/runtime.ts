import type { WebConfig } from './config';

let current: WebConfig | undefined;

/** Stores the configuration validated at startup by `hooks.server.ts`. */
export function setConfig(config: WebConfig): void {
  current = config;
}

/** The configuration validated at startup. */
export function getConfig(): WebConfig {
  if (current === undefined) {
    throw new Error('The web app configuration has not been loaded.');
  }
  return current;
}
