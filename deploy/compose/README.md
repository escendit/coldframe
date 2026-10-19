# Compose reference example

> **Reference example only, not the supported deployment (NFR7).** The supported path is
> single-node RKE2 with Fleet: [`docs/deploy/`](../../docs/deploy/README.md). Nothing here is
> tested beyond `docker compose config`, and no upgrade, backup or restore path exists for it.

[`compose.yaml`](compose.yaml) runs the released images the way the Helm charts in
[`deploy/charts`](../charts/README.md) do, on one Docker host: PostgreSQL 18.6 with the databases
`coldframe`, `temporal`, `temporal_visibility` and `keycloak` ([`postgres-init.sh`](postgres-init.sh)),
NATS 2.15.0 with JetStream, the Temporal schema and namespace one-shots and Temporal Server 1.31.2,
Phase Two Keycloak, the migration one-shot, the Server and the web app. It is useful to read how the
pieces fit together, or to try the web app on a LAN before you set up the real thing.

What it does not have:

- **No TLS.** Everything is plain HTTP on the LAN: the Server trusts an `http://` issuer
  (`Identity__RequireHttpsMetadata=false`), the web app talks to Keycloak over HTTP
  (`KEYCLOAK_ALLOW_INSECURE_HTTP=true`) and its session cookie is not `Secure`. Never expose these
  ports to the internet.
- **No backups.** The data lives in the Docker volumes `postgres` and `nats`; `docker compose down -v`
  deletes it.
- **The web app only.** Release builds of the mobile apps need HTTPS with a public certificate, and
  no push credentials are wired in ([`docs/build/apps.md`](../../docs/build/apps.md)).

## Ports

| Port on the Docker host | Service |
| --- | --- |
| 3000 | the web app: `http://<COLDFRAME_HOST>:3000` |
| 8080 | the Server |
| 8180 | Keycloak: `http://<COLDFRAME_HOST>:8180`, issuer `http://<COLDFRAME_HOST>:8180/realms/coldframe` |

`COLDFRAME_HOST` is one LAN name or IP address of the Docker host. The browser and the containers
both use it, so the issuer in a token is the same string the Server and the web app expect; the
containers must be able to reach the host's published ports under that name.

## Start it

From the repository root, at the release tag you run (`git checkout vX.Y.Z`), with Docker Compose
v2.23 or later and `jq`:

1. Write the realm for your origin. The origin must be exactly `http://<COLDFRAME_HOST>:3000`,
   with the same `COLDFRAME_HOST` as in `.env` (step 2); a mismatch only shows as a `redirect_uri`
   error at the first sign-in, after the one-time import. `--allow-http` exists for this example
   only:

   ```sh
   deploy/realm/prepare-realm.sh --allow-http --web-origin http://<COLDFRAME_HOST>:3000 \
     --out deploy/compose/coldframe-realm.json
   ```

2. Copy the settings and replace every value (git ignores `.env`; never commit it):

   ```sh
   cp deploy/compose/.env.example deploy/compose/.env
   $EDITOR deploy/compose/.env
   ```

   `COLDFRAME_VERSION` is the release without the `v` (there is no default and no `latest`).
   Generate the secrets with `openssl rand -hex 32` and the Device key-encryption key with
   `openssl rand -base64 32`; record that key apart from this machine, since no enrolled Device works
   without it. The enrolment key goes on one line with `\n` between its lines:

   ```sh
   printf 'ENROLMENT_PRIVATE_KEY_PEM="%s"\n' "$(openssl genpkey -algorithm X25519 | awk '{printf "%s\\n", $0}')"
   ```

3. Start it:

   ```sh
   docker compose -f deploy/compose/compose.yaml --env-file deploy/compose/.env up -d
   docker compose -f deploy/compose/compose.yaml --env-file deploy/compose/.env ps -a
   ```

   `temporal-schema`, `temporal-namespace` and `migrations` run once and exit 0; the rest keep
   running. On a host with SELinux (Fedora, RHEL) the containers cannot read the bind-mounted
   `postgres-init.sh` and `coldframe-realm.json` until you relabel them
   (`chcon -t container_file_t deploy/compose/postgres-init.sh deploy/compose/coldframe-realm.json`).

4. Open `http://<COLDFRAME_HOST>:3000`, Register, verify the email (the SMTP values of `.env`) and
   create a Site, as in [section 6 of the deploy guide](../../docs/deploy/README.md#6-first-sign-in).

The realm is imported on the first start only; to change it later use the Keycloak admin console
(`http://<COLDFRAME_HOST>:8180/admin`, the bootstrap admin of `.env`), or start over with
`docker compose ... down -v`, which deletes every volume.
