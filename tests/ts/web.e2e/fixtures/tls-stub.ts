import { createServer, type Server } from 'node:https';
import { generate } from 'selfsigned';

/** An HTTPS server whose certificate nobody trusts: every TLS client must refuse it. */
export async function startTlsStub(port: number): Promise<{ close(): Promise<void> }> {
  const pems = await generate([{ name: 'commonName', value: 'localhost' }], { keySize: 2048, notAfterDate: new Date(Date.now() + 2 * 24 * 60 * 60 * 1000) });
  const server: Server = createServer({ key: pems.private, cert: pems.cert }, (_request, response) => {
    response.writeHead(200, { 'content-type': 'text/plain' });
    response.end('Healthy');
  });
  await new Promise<void>((resolve) => server.listen(port, resolve));
  return {
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => {
          resolve();
        });
      }),
  };
}
