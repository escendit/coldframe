/**
 * Starts the e2e stack: the fake IdP, the self-signed HTTPS stub and three `node build`
 * instances of the web app (Server reachable; Server on a closed port; Server on the TLS stub).
 * Playwright runs this as its web server and stops it when the run ends.
 */
import { spawn, type ChildProcess } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { clientId, clientSecret, startFakeIdp } from './fake-idp.ts';
import { idpOrigin, ports } from './ports.ts';
import { startTlsStub } from './tls-stub.ts';

const webApp = fileURLToPath(new URL('../../../../apps/ts/web', import.meta.url));

const idp = await startFakeIdp(ports.idp);
const tls = await startTlsStub(ports.tls);

function startApp(port: number, serverUrl: string): ChildProcess {
  const child = spawn(process.execPath, ['build'], {
    cwd: webApp,
    stdio: ['ignore', 'inherit', 'inherit'],
    env: {
      ...process.env,
      PORT: String(port),
      ORIGIN: `http://localhost:${String(port)}`,
      COLDFRAME_SERVER_URL: serverUrl,
      KEYCLOAK_ISSUER: idp.issuer,
      KEYCLOAK_CLIENT_ID: clientId,
      KEYCLOAK_CLIENT_SECRET: clientSecret,
      KEYCLOAK_ALLOW_INSECURE_HTTP: 'true',
    },
  });
  child.on('exit', (code, signal) => {
    if (signal === null && code !== 0) {
      console.error(`web app on ${String(port)} exited with ${String(code)}`);
      process.exit(1);
    }
  });
  return child;
}

const apps = [
  startApp(ports.app, idpOrigin),
  startApp(ports.unreachable, `http://localhost:${String(ports.closed)}`),
  startApp(ports.untrusted, `https://localhost:${String(ports.tls)}`),
];

async function stop(): Promise<void> {
  for (const app of apps) {
    app.kill('SIGTERM');
  }
  await Promise.all([idp.close(), tls.close()]);
  process.exit(0);
}

process.on('SIGTERM', () => void stop());
process.on('SIGINT', () => void stop());
