import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import PauseModal from '$lib/components/PauseModal.svelte';
import ResumeControl from '$lib/components/ResumeControl.svelte';
import { devicePauseOf, isCalendarDate, pauseAccessOf, pauseAttempts, pauseDateRefused, pauseNoticeOf, pausePrimaryLabel, pauseScopeOptions, pauseScopes, type PauseDevice, type PauseFailure } from '$lib/pause';
import { pauseAction, pauseActions, pauseDevice, pauseSite, resumeDevice, resumeSite } from '$lib/server/pause';
import { fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { filesUnder, read, rel, webSrc } from './helpers.ts';

/** Story 8.2: the Pause modal, the four Server calls and the two form actions every pause surface posts to. */
const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { name: 'Simon Novak' } } } };
const siteId = '0192a000-0000-7000-8000-00000000000a';
const nodeId = '7c19000000000001';
const node: PauseDevice = { id: nodeId, kind: 'node' };
const view = { locale: 'en-GB', timeZone: 'UTC' } as const;

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

function formOf(fields: Record<string, string>): Request {
  return new Request('http://localhost/garden', { method: 'POST', body: new URLSearchParams(fields), headers: { 'content-type': 'application/x-www-form-urlencoded' } });
}

async function redirectOf(action: () => unknown): Promise<{ status: number; location: string }> {
  try {
    await action();
  } catch (error) {
    if (isRedirect(error)) {
      return { status: error.status, location: error.location };
    }
    throw error;
  }
  throw new Error('Expected a redirect.');
}

function modal(props: { device?: PauseDevice | null; failure?: PauseFailure | null; until?: string } = {}): string {
  return render(PauseModal, { props: { siteId, siteName: 'Home garden', device: props.device ?? null, action: '/garden?/pause', failure: props.failure ?? null, until: props.until ?? '' } }).body;
}

describe('Pause scope and labels', () => {
  test('UX-DR46 UX-DR22 from the Site menu the scope is Whole Site alone, and the primary names the Site', () => {
    expect(pauseScopes(null)).toEqual(['site']);
    expect(pauseScopeOptions(null)).toEqual([{ value: 'site', label: 'Whole Site' }]);
    expect(pausePrimaryLabel('site', 'Home garden', null)).toBe('Pause Home garden');
  });

  test('UX-DR46 UX-DR70 from a Device the scope is This Device, then Whole Site, and the primary follows it', () => {
    expect(pauseScopes(node)).toEqual(['device', 'site']);
    expect(pauseScopeOptions(node)).toEqual([
      { value: 'device', label: 'This Device' },
      { value: 'site', label: 'Whole Site' },
    ]);
    expect(pausePrimaryLabel('device', 'Home garden', node)).toBe('Pause 7C19');
    expect(pausePrimaryLabel('site', 'Home garden', node)).toBe('Pause Home garden');
  });

  test('UX-DR46 the end is the calendar date as picked: YYYY-MM-DD and nothing else', () => {
    expect(isCalendarDate('2027-03-01')).toBe(true);
    for (const value of ['', '1 Mar 2027', '2027-3-1', '2027-03-01T00:00:00Z', '2027-13-01', '2027-02-30', '03/01/2027']) {
      expect(isCalendarDate(value), value).toBe(false);
    }
  });

  test('UX-DR84 Pause and Resume are for Administrators and Owners', () => {
    expect([pauseAccessOf('Owner'), pauseAccessOf('Administrator'), pauseAccessOf('Member')]).toEqual([true, true, false]);
  });
});

describe('Paused Devices', () => {
  test('UX-DR70 a Device that is not paused offers Pause to an Administrator and shows no paused state', () => {
    expect(devicePauseOf({ kind: 'node' }, { ...view, admin: true })).toEqual({ paused: false, label: null, until: null, canPause: true, canResume: false, resumeSiteLine: null });
    expect(devicePauseOf({ kind: 'hub', pausedBy: [] }, { ...view, admin: true }).paused).toBe(false);
  });

  test('UX-DR70 paused by itself: Paused, the end from the Server, and Resume in one tap', () => {
    expect(devicePauseOf({ kind: 'node', pausedBy: ['device'], pausedUntil: '2027-03-01T00:00:00.000Z' }, { ...view, admin: true })).toEqual({
      paused: true,
      label: 'Paused',
      until: 'until 1 Mar',
      canPause: false,
      canResume: true,
      resumeSiteLine: null,
    });
    expect(devicePauseOf({ kind: 'hub', pausedBy: ['device'] }, { ...view, admin: true }).until).toBeNull();
  });

  test('UX-DR70 UX-DR83 paused by the Site only: Paused by Site, no Resume for the Device, the line, and Pause stays', () => {
    expect(devicePauseOf({ kind: 'node', pausedBy: ['site'], pausedUntil: '2027-03-01T00:00:00.000Z' }, { ...view, admin: true })).toEqual({
      paused: true,
      label: 'Paused by Site',
      until: 'until 1 Mar',
      canPause: true,
      canResume: false,
      resumeSiteLine: 'Resume the Site to resume this Node',
    });
    expect(devicePauseOf({ kind: 'hub', pausedBy: ['site'] }, { ...view, admin: true }).resumeSiteLine).toBe('Resume the Site to resume this Hub');
  });

  test('UX-DR70 paused by both: Resume lifts the Device, which stays paused by the Site', () => {
    expect(devicePauseOf({ kind: 'node', pausedBy: ['device', 'site'] }, { ...view, admin: true })).toMatchObject({ label: 'Paused by Site', canPause: false, canResume: true, resumeSiteLine: null });
  });

  test('UX-DR84 a Member sees the same paused state and no control or hint', () => {
    for (const pausedBy of [undefined, ['device'], ['site'], ['device', 'site']] as const) {
      expect(devicePauseOf({ kind: 'node', pausedBy }, { ...view, admin: false })).toMatchObject({ canPause: false, canResume: false, resumeSiteLine: null });
    }
    expect(devicePauseOf({ kind: 'node', pausedBy: ['site'] }, { ...view, admin: false })).toMatchObject({ paused: true, label: 'Paused by Site' });
  });

  test('UX-DR70 an end the client cannot read is left out, and paused is the Server\'s list, never a guess', () => {
    expect(devicePauseOf({ kind: 'node', pausedBy: ['device'], pausedUntil: 'soon' }, { ...view, admin: true }).until).toBeNull();
    // An end without a source is not a Pause.
    expect(devicePauseOf({ kind: 'node', pausedUntil: '2027-03-01T00:00:00.000Z' }, { ...view, admin: true })).toMatchObject({ paused: false, label: null, until: null });
  });
});

describe('Pause notices', () => {
  test('UX-DR84 a 403 says who can change it, by the Site name, with no retry', () => {
    expect(pauseNoticeOf({ notice: 'forbidden', scope: 'site' }, 'Home garden')).toEqual({ message: "You can't change this on Home garden. Ask an Owner or Administrator.", tryAgain: false });
  });

  test('UX-DR70 a Server that cannot be reached and an answer nobody expected keep their notice with Try again; a certificate never retries', () => {
    expect(pauseNoticeOf({ notice: 'unreachable', scope: 'site' }, 'Home garden')).toEqual({ message: "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.", tryAgain: true });
    expect(pauseNoticeOf({ notice: 'unexpected', scope: 'device' }, 'Home garden')).toEqual({ message: "That didn't work. Try again.", tryAgain: true });
    expect(pauseNoticeOf({ notice: 'certificate', scope: 'site' }, 'Home garden')?.tryAgain).toBe(false);
  });

  test('UX-DR70 a 404 names what is gone: the Device, or the Site', () => {
    expect(pauseNoticeOf({ notice: 'notFound', scope: 'device' }, 'Home garden')).toEqual({ message: 'That Device is gone. Reload the page.', tryAgain: false });
    expect(pauseNoticeOf({ notice: 'notFound', scope: 'site' }, 'Home garden')?.message).toBe('Home garden is no longer one of your Sites. Nothing was changed.');
  });

  test('UX-DR46 a refused date is said under the field, not as a notice', () => {
    expect(pauseNoticeOf({ notice: 'date', scope: 'site' }, 'Home garden')).toBeNull();
  });
});

describe('A Pause modal between openings', () => {
  test('UX-DR46 a refused date keeps its mark only until the date is edited', () => {
    const refused: PauseFailure = { action: 'pause', scope: 'site', notice: 'date', until: '2020-01-01' };
    expect(pauseDateRefused(refused, '2020-01-01')).toBe(true);
    expect(pauseDateRefused(refused, '2027-03-01')).toBe(false);
    expect(pauseDateRefused(refused, '')).toBe(false);
    // Another refusal is never the date's, and no refusal marks nothing.
    expect(pauseDateRefused({ notice: 'forbidden', until: '2020-01-01' }, '2020-01-01')).toBe(false);
    expect(pauseDateRefused(null, '')).toBe(false);
    const html = render(PauseModal, { props: { open: true, siteId, siteName: 'Home garden', device: null, action: '?/pause', failure: refused, until: '2027-03-01' } }).body;
    expect(text(html)).not.toContain('Choose a date after today.');
    expect(html).not.toContain('aria-invalid="true"');
  });

  test('UX-DR70 an answer that arrives after Cancel or Esc belongs to no opening: its refusal is not shown in the next one', () => {
    const attempts = pauseAttempts();
    const first = attempts.begin();
    expect(first()).toBe(true);
    attempts.abandon();
    expect(first()).toBe(false);
    // The next opening's own save is current, and stays so while the old answer is ignored.
    const second = attempts.begin();
    expect(second()).toBe(true);
    expect(first()).toBe(false);
    const modal = read(`${webSrc}/lib/components/PauseModal.svelte`);
    expect(modal).toMatch(/function closed\(\): void \{[\s\S]*?attempts\.abandon\(\);/u);
    expect(modal).toMatch(/else if \(!current\(\)\) \{[\s\S]*?\} else if \(result\.type === 'failure'/u);
    // A success still reads the page again, whether or not the modal was closed meanwhile.
    expect(modal).toMatch(/result\.type === 'success'\) \{[\s\S]*?await invalidateAll\(\);[\s\S]*?else if \(!current\(\)\)/u);
  });

  test('UX-DR22 the Site menu sends one Resume at a time', () => {
    const control = read(`${webSrc}/lib/components/ResumeControl.svelte`);
    expect(control).toMatch(/export function submit\(\): void \{\s*if \(working\) \{\s*return;\s*\}\s*form\?\.requestSubmit\(\);/u);
  });
});

describe('The pause calls (AD-14)', () => {
  test('UX-DR70 pause and resume of a Site: PUT with an empty body or the date, DELETE, each with the token', async () => {
    const fake = fakeServer((request) => jsonResponse(200, request.method === 'DELETE' ? { paused: false } : { paused: true }));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await pauseSite(locals, siteId, null, dependencies)).toEqual({ ok: { paused: true } });
    await pauseSite(locals, siteId, '2027-03-01', dependencies);
    expect(await resumeSite(locals, siteId, dependencies)).toEqual({ ok: { paused: false } });
    expect(fake.seen).toEqual([
      { method: 'PUT', path: `/sites/${siteId}/pause`, authorization: 'Bearer access-token-1', key: null, body: '{}' },
      { method: 'PUT', path: `/sites/${siteId}/pause`, authorization: 'Bearer access-token-1', key: null, body: '{"until":"2027-03-01"}' },
      { method: 'DELETE', path: `/sites/${siteId}/pause`, authorization: 'Bearer access-token-1', key: null, body: '' },
    ]);
  });

  test('UX-DR70 pause and resume of a Device', async () => {
    const fake = fakeServer((request) => jsonResponse(200, request.method === 'DELETE' ? { pausedBy: ['site'] } : { pausedBy: ['device', 'site'] }));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await pauseDevice(locals, siteId, nodeId, '2026-11-01', dependencies)).toEqual({ ok: { pausedBy: ['device', 'site'] } });
    await pauseDevice(locals, siteId, nodeId, null, dependencies);
    expect(await resumeDevice(locals, siteId, nodeId, dependencies)).toEqual({ ok: { pausedBy: ['site'] } });
    expect(fake.seen.map((request) => [request.method, request.path, request.body])).toEqual([
      ['PUT', `/sites/${siteId}/devices/${nodeId}/pause`, '{"until":"2026-11-01"}'],
      ['PUT', `/sites/${siteId}/devices/${nodeId}/pause`, '{}'],
      ['DELETE', `/sites/${siteId}/devices/${nodeId}/pause`, ''],
    ]);
  });
});

describe('The pause form actions', () => {
  const ok = (): ReturnType<typeof fakeServer> => fakeServer((request) => jsonResponse(200, new URL(request.url).pathname.endsWith(`/sites/${siteId}/pause`) ? { paused: request.method === 'PUT' } : { pausedBy: request.method === 'PUT' ? ['device'] : [] }));

  test('UX-DR70 Whole Site with no date puts {} and succeeds', async () => {
    const fake = ok();
    expect(await pauseAction('pause', locals, formOf({ siteId, scope: 'site', until: '' }), { serverUrl, fetch: fake.fetch })).toEqual({ action: 'pause', scope: 'site', done: true });
    expect(fake.seen.map((request) => [request.method, request.path, request.body])).toEqual([['PUT', `/sites/${siteId}/pause`, '{}']]);
  });

  test('UX-DR46 a picked date is sent as the calendar date, unchanged', async () => {
    const fake = ok();
    await pauseAction('pause', locals, formOf({ siteId, scope: 'site', until: '2027-03-01' }), { serverUrl, fetch: fake.fetch });
    expect(fake.seen.map((request) => request.body)).toEqual(['{"until":"2027-03-01"}']);
  });

  test('UX-DR70 This Device pauses the Device and leaves the Site alone; Whole Site from a Device pauses the Site only', async () => {
    const fake = ok();
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await pauseAction('pause', locals, formOf({ siteId, scope: 'device', deviceId: nodeId, until: '2026-11-01' }), dependencies)).toEqual({ action: 'pause', scope: 'device', deviceId: nodeId, done: true });
    expect(await pauseAction('pause', locals, formOf({ siteId, scope: 'site', deviceId: nodeId, until: '' }), dependencies)).toEqual({ action: 'pause', scope: 'site', done: true });
    expect(fake.seen.map((request) => [request.method, request.path, request.body])).toEqual([
      ['PUT', `/sites/${siteId}/devices/${nodeId}/pause`, '{"until":"2026-11-01"}'],
      ['PUT', `/sites/${siteId}/pause`, '{}'],
    ]);
  });

  test('UX-DR70 Resume is one call: DELETE for the Site or for the Device', async () => {
    const fake = ok();
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await pauseAction('resume', locals, formOf({ siteId, scope: 'site' }), dependencies)).toEqual({ action: 'resume', scope: 'site', done: true });
    expect(await pauseAction('resume', locals, formOf({ siteId, scope: 'device', deviceId: nodeId }), dependencies)).toEqual({ action: 'resume', scope: 'device', deviceId: nodeId, done: true });
    expect(fake.seen.map((request) => [request.method, request.path])).toEqual([
      ['DELETE', `/sites/${siteId}/pause`],
      ['DELETE', `/sites/${siteId}/devices/${nodeId}/pause`],
    ]);
  });

  test('UX-DR46 a 400 for the date is the date refusal, with what was picked', async () => {
    const fake = fakeServer(() => problemResponse(400, 'validation'));
    const result = (await pauseAction('pause', locals, formOf({ siteId, scope: 'site', until: '2020-01-01' }), { serverUrl, fetch: fake.fetch })) as { status: number; data: unknown };
    expect(result.status).toBe(400);
    expect(result.data).toEqual({ action: 'pause', scope: 'site', notice: 'date', until: '2020-01-01' });
  });

  test('UX-DR46 a date that is no calendar date is refused before the Server is asked; a 400 without a date is not a date refusal', async () => {
    const fake = fakeServer(() => problemResponse(400, 'validation'));
    const dependencies = { serverUrl, fetch: fake.fetch };
    const malformed = (await pauseAction('pause', locals, formOf({ siteId, scope: 'site', until: 'tomorrow' }), dependencies)) as { status: number; data: unknown };
    expect(malformed).toMatchObject({ status: 400, data: { action: 'pause', scope: 'site', notice: 'date', until: 'tomorrow' } });
    expect(fake.seen).toEqual([]);
    const undated = (await pauseAction('pause', locals, formOf({ siteId, scope: 'device', deviceId: nodeId, until: '' }), dependencies)) as { data: { notice: string } };
    expect(undated.data.notice).toBe('unexpected');
  });

  test('UX-DR84 UX-DR70 the refusals: 403, 404, no answer, an untrusted certificate, anything else; nothing is kept', async () => {
    const fields = { siteId, scope: 'device', deviceId: nodeId, until: '2026-11-01' };
    const refusals: [number, string, number][] = [
      [403, 'forbidden', 403],
      [404, 'notFound', 404],
      [500, 'unexpected', 502],
      [503, 'unexpected', 502],
    ];
    for (const [status, notice, failureStatus] of refusals) {
      for (const action of ['pause', 'resume'] as const) {
        const fake = fakeServer(() => problemResponse(status, 'x'));
        const result = (await pauseAction(action, locals, formOf(fields), { serverUrl, fetch: fake.fetch })) as { status: number; data: unknown };
        expect(result.status, `${String(status)} ${action}`).toBe(failureStatus);
        expect(result.data).toEqual({ action, scope: 'device', deviceId: nodeId, notice, ...(action === 'pause' ? { until: '2026-11-01' } : {}) });
      }
    }
    const unreachable = (await pauseAction('pause', locals, formOf(fields), { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) })) as { status: number; data: { notice: string; until: string } };
    expect(unreachable).toMatchObject({ status: 502, data: { notice: 'unreachable', until: '2026-11-01' } });
    const certificate = (await pauseAction('resume', locals, formOf(fields), { serverUrl, fetch: () => Promise.reject(fetchFailed('CERT_HAS_EXPIRED')) })) as { data: { notice: string } };
    expect(certificate.data.notice).toBe('certificate');
  });

  test('UX-DR70 a form the page did not send is refused before the Server is asked; a 401 signs out', async () => {
    const fake = ok();
    const dependencies = { serverUrl, fetch: fake.fetch };
    for (const fields of [{ siteId, scope: 'lot' }, { siteId, scope: 'device' }, { siteId: '', scope: 'site' }]) {
      const result = (await pauseAction('pause', locals, formOf(fields), dependencies)) as { status: number; data: { notice: string } };
      expect(result.status).toBe(400);
      expect(result.data.notice).toBe('unexpected');
    }
    expect(fake.seen).toEqual([]);
    const expired = await redirectOf(() => pauseAction('pause', locals, formOf({ siteId, scope: 'site' }), { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
  });

  test('UX-DR70 the overview, Lot detail and Devices carry the same two actions, and the browser never calls the Server', () => {
    expect(Object.keys(pauseActions)).toEqual(['pause', 'resume']);
    for (const route of ['garden', 'garden/[lotId]', 'devices']) {
      const source = read(`${webSrc}/routes/(app)/${route}/+page.server.ts`);
      expect(source, route).toMatch(/pauseActions/u);
    }
    // AD-14: the pause endpoints are named by the server module alone.
    const callers = filesUnder(webSrc, ['.svelte', '.ts']).filter((file) => /\{siteId\}(?:\/devices\/\{deviceId\})?\/pause\b/u.test(read(file))).map(rel);
    expect(callers).toEqual(['apps/ts/web/src/lib/server/pause.ts']);
  });
});

describe('Pause modal', () => {
  test('UX-DR46 UX-DR76 a native dialog named Pause, with the scope, the Until date and its helper, Cancel and the result', () => {
    const body = modal();
    expect(body).toMatch(/<dialog[^>]*aria-labelledby="cf-pause-title"/u);
    expect(body).toMatch(/<h2[^>]*id="cf-pause-title"[^>]*>Pause<\/h2>/u);
    expect(body).toMatch(/<form[^>]*method="POST"[^>]*action="\/garden\?\/pause"/u);
    expect(body).toMatch(/<label[^>]*for="cf-pause-until"[^>]*>Until<\/label>/u);
    expect(body).toMatch(/<input[^>]*id="cf-pause-until"[^>]*name="until"[^>]*type="date"/u);
    expect(body).toMatch(/<input[^>]*aria-describedby="cf-pause-until-helper"/u);
    expect(text(body)).toBe('Pause Pause scope Whole Site Until Leave empty to pause until someone resumes. Cancel Pause Home garden');
    expect(body).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Pause Home garden<\/span>/u);
    expect(body).not.toMatch(/disabled/u);
  });

  test('UX-DR46 UX-DR22 from the Site menu Whole Site is the only segment, selected, and the form carries the Site alone', () => {
    const body = modal();
    expect(body.match(/aria-pressed=/gu)).toHaveLength(1);
    expect(body).toMatch(/aria-pressed="true"[\s\S]*?Whole Site/u);
    expect(body).toContain(`name="siteId" value="${siteId}"`);
    expect(body).toContain('name="scope" value="site"');
    expect(body).not.toContain('name="deviceId"');
  });

  test('UX-DR46 UX-DR70 from a Device This Device is selected beside Whole Site, and the primary names the Device', () => {
    const body = modal({ device: node });
    const segments = [...body.matchAll(/<button[^>]*aria-pressed="(true|false)"[^>]*>([\s\S]*?)<\/button>/gu)].map((match) => [text(match[2] ?? ''), match[1]]);
    expect(segments).toEqual([
      ['This Device', 'true'],
      ['Whole Site', 'false'],
    ]);
    expect(body).toContain('name="scope" value="device"');
    expect(body).toContain(`name="deviceId" value="${nodeId}"`);
    expect(text(body)).toMatch(/Cancel Pause 7C19$/u);
  });

  test('UX-DR46 a refused date marks the field with the reason and keeps what was picked', () => {
    const body = modal({ failure: { action: 'pause', scope: 'site', notice: 'date', until: '2020-01-01' }, until: '2020-01-01' });
    expect(body).toMatch(/<input[^>]*id="cf-pause-until"[^>]*aria-invalid="true"/u);
    expect(body).toMatch(/id="cf-pause-until-reason"[^>]*>Choose a date after today\./u);
    expect(body).toMatch(/<input[^>]*name="until"[^>]*value="2020-01-01"/u);
    expect(text(body)).not.toContain('Leave empty');
    expect(body).not.toContain('cf-inline-notice');
  });

  test('UX-DR84 a 403 says who can change it, inside the modal, with no retry', () => {
    const body = modal({ failure: { action: 'pause', scope: 'site', notice: 'forbidden' } });
    const notice = /<div id="cf-pause-notice" class="cf-inline-notice[^"]*">[\s\S]*?<\/div>/u.exec(body)?.[0] ?? '';
    expect(text(notice)).toBe("You can't change this on Home garden. Ask an Owner or Administrator.");
    expect(body).not.toMatch(/Try again/u);
  });

  test('UX-DR70 a Server out of reach keeps the entries and its notice with Try again, which sends the form again', () => {
    const body = modal({ device: node, failure: { action: 'pause', scope: 'device', deviceId: nodeId, notice: 'unreachable', until: '2026-11-01' }, until: '2026-11-01' });
    expect(text(body)).toContain("Can't reach your Coldframe Server.");
    expect(body).toMatch(/<input[^>]*name="until"[^>]*value="2026-11-01"/u);
    // Try again is a submit button of the modal's own form.
    const form = /<form[\s\S]*<\/form>/u.exec(body)?.[0] ?? '';
    expect(form).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Try again<\/span>/u);
    expect(body).not.toMatch(/aria-invalid/u);
  });

  test('UX-DR76 the modal takes the one modal level and never opens over another', () => {
    const source = read(`${webSrc}/lib/components/PauseModal.svelte`);
    expect(source).toMatch(/claimModal\(dialog\)/u);
    expect(source).toMatch(/releaseModal\(/u);
    expect(source).toContain('showModal()');
  });

  test('UX-DR70 a save reads the page again from the Server and keeps nothing itself', () => {
    const source = read(`${webSrc}/lib/components/PauseModal.svelte`);
    expect(source).toContain('invalidateAll()');
    expect(source).not.toMatch(/localStorage|sessionStorage/u);
  });
});

describe('Resume control', () => {
  const props = { id: 'cf-resume-test', action: '?/resume', fields: { siteId, scope: 'device', deviceId: nodeId }, label: 'Resume 7C19', name: '7C19', siteName: 'Home garden' };

  test('UX-DR70 Resume is one button in its own form: no modal and nothing to fill in', () => {
    const { body } = render(ResumeControl, { props });
    expect(body).toMatch(/<form[^>]*method="POST"[^>]*action="\?\/resume"/u);
    expect(body).toContain('name="scope" value="device"');
    expect(body).toContain(`name="deviceId" value="${nodeId}"`);
    expect(body.match(/<button/gu)).toHaveLength(1);
    expect(body).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Resume 7C19<\/span>/u);
    expect(body).not.toMatch(/<dialog|type="date"|cf-inline-notice/u);
  });

  test('UX-DR70 a Resume that did not happen shows its notice with Try again, which sends the same Resume', () => {
    const { body } = render(ResumeControl, { props: { ...props, failure: { action: 'resume', scope: 'device', deviceId: nodeId, notice: 'unreachable' } as const } });
    const retry = /<form id="cf-resume-test-notice-retry"[\s\S]*?<\/form>/u.exec(body)?.[0] ?? '';
    expect(retry).toContain('action="?/resume"');
    expect(retry).toContain(`name="deviceId" value="${nodeId}"`);
    expect(text(retry)).toBe("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi. Try again");
  });

  test('UX-DR84 a 403 on Resume has the notice and no retry', () => {
    const { body } = render(ResumeControl, { props: { ...props, failure: { action: 'resume', scope: 'device', deviceId: nodeId, notice: 'forbidden' } as const } });
    expect(text(body)).toContain("You can't change this on Home garden. Ask an Owner or Administrator.");
    expect(body).not.toContain('Try again');
  });

  test('UX-DR22 the Site menu resumes through a form without a button of its own', () => {
    const { body } = render(ResumeControl, { props: { ...props, fields: { siteId, scope: 'site' }, hidden: true } });
    expect(body).toMatch(/<form[^>]*class="[^"]*cf-resume--hidden/u);
    expect(body).not.toMatch(/<button/u);
  });
});
