/**
 * The Coldframe REST client. Types come only from `src/schema.ts`, which openapi-typescript
 * generates from `packages/openapi` (AD-10); nothing here restates an API type by hand.
 */
import createClient, { type Client } from 'openapi-fetch';
import type { components, paths } from './schema';

export type { components, operations, paths } from './schema';

export type Site = components['schemas']['Site'];
export type SiteList = components['schemas']['SiteList'];
export type SiteRole = components['schemas']['SiteRole'];
export type CreateSiteRequest = components['schemas']['CreateSiteRequest'];
export type RenameSiteRequest = components['schemas']['RenameSiteRequest'];
export type Lot = components['schemas']['Lot'];
export type LotList = components['schemas']['LotList'];
export type LotStatus = components['schemas']['LotStatus'];
export type NodeStatus = components['schemas']['NodeStatus'];
export type SensorReading = components['schemas']['SensorReading'];
export type Calibration = components['schemas']['Calibration'];
export type CalibrationState = components['schemas']['CalibrationState'];
export type CalibrationReading = components['schemas']['CalibrationReading'];
export type CalibrateSensorRequest = components['schemas']['CalibrateSensorRequest'];
export type SensorQuantity = components['schemas']['SensorQuantity'];
export type SensorUnit = components['schemas']['SensorUnit'];
export type ChargeState = components['schemas']['ChargeState'];
export type LotHistory = components['schemas']['LotHistory'];
export type LotHistoryDay = components['schemas']['LotHistoryDay'];
export type CreateLotRequest = components['schemas']['CreateLotRequest'];
export type RenameLotRequest = components['schemas']['RenameLotRequest'];
export type DeviceKind = components['schemas']['DeviceKind'];
export type DeviceListItem = components['schemas']['DeviceListItem'];
export type DeviceList = components['schemas']['DeviceList'];
export type ProblemDetails = components['schemas']['ProblemDetails'];

export type ColdframeClient = Client<paths>;

export interface ColdframeClientOptions {
  /** The Server's base URL. */
  readonly baseUrl: string | URL;
  /** The caller's Keycloak access token, sent as `Authorization: Bearer`. */
  readonly accessToken: string;
  /** Injectable for tests and for SvelteKit's `fetch`. */
  readonly fetch?: (request: Request) => Promise<Response>;
}

/** A typed client for one caller. Every request carries the caller's bearer token. */
export function createColdframeClient(options: ColdframeClientOptions): ColdframeClient {
  const baseUrl = typeof options.baseUrl === 'string' ? options.baseUrl : options.baseUrl.href;
  return createClient<paths>({
    baseUrl: baseUrl.replace(/\/+$/u, ''),
    headers: { authorization: `Bearer ${options.accessToken}` },
    ...(options.fetch === undefined ? {} : { fetch: options.fetch }),
  });
}
