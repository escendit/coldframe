# Developer quickstart

This page takes you from a fresh clone to a running local stack and a green test run in every
language. It covers development only. Deployment is described in [`deploy/`](../deploy).

## Prerequisites

Install only what you need for the language you work on. The local stack needs the first two rows.

| Tool | Version | Used for | Pinned in |
| --- | --- | --- | --- |
| .NET SDK | 10.0.1xx | Server, Aspire AppHost, integration tests | `global.json` |
| Docker or Podman | any current release | Containers of the local stack | |
| Rust, through `rustup` | 1.97.1, installed on first use | Shared crates | `rust-toolchain.toml` |
| Node.js | 24 | TypeScript packages | `.node-version` |
| pnpm | 12.6.0 | TypeScript packages | `package.json` |
| JDK | 25 | Kotlin modules; the wrapper downloads Gradle 9.8.0 | `packages/kt/core/build.gradle.kts` |
| Swift | 6.0 or later | Swift packages; Xcode 26 on macOS, a container on Linux | `Package.swift` |
| Android SDK | Platform `android-37.0`, Build-Tools 36.0.0 | Android app and the Android targets of the Kotlin modules | `gradle/libs.versions.toml` |
| Xcode and XcodeGen | Xcode 26, XcodeGen 2.46.0 | iOS app (macOS only) | `apps/swift/ios/project.yml` |

Nothing needs an account or a token. Every dependency comes from a public registry or repository.

## Run the local stack

The Aspire AppHost in [`aspire/Coldframe.AppHost`](../aspire/Coldframe.AppHost) starts everything
the Server depends on, then the Server itself.

With Docker:

```sh
dotnet run --project aspire/Coldframe.AppHost
```

With Podman:

```sh
ASPIRE_CONTAINER_RUNTIME=podman dotnet run --project aspire/Coldframe.AppHost
```

The first build downloads the Aspire CLI that matches the AppHost, and the first start builds the
Keycloak image. Both take a few minutes once. The command prints the address of the Aspire
dashboard, which shows every resource, its state, its logs and its endpoints. Stop the stack with
`Ctrl+C`; the containers are removed and no data is kept.

| Resource | What runs | Version |
| --- | --- | --- |
| `postgres` | PostgreSQL, with the databases `coldframe` and `keycloak` | 18.6 |
| `nats` | NATS with JetStream | 2.15.0 |
| `temporal` | Temporal CLI development server, namespace `coldframe` | CLI 1.8.3, Server 1.31.2 |
| `keycloak` | Phase Two Keycloak with `keycloak-temporal-extensions` v0.0.1-rc.2 | 26.6.7 |
| `migrations` | The migration job: creates or updates the schema of `coldframe`, then exits | built from `apps/cs/migrations` |
| `server` | The Server: Orleans silo and Edge API | built from `apps/cs/server` |

The `migrations` job runs first and shows as *Finished* once it has applied the schema; the Server
waits for it and does not start when it fails. The Server reports its health at `/.well-known/healthz`, with `/ready`, `/live` and `/startup`
below it. The answer lists the check `silo`, which is healthy while the silo is an active member
of its cluster.

Things to know:

- **Ports change on every start**, except the dashboard on `http://localhost:15080` and the Server
  on `http://localhost:5080`. Take the other addresses from the dashboard.
- **The Keycloak administrator is `admin`.** Its password is generated on every start. Read it in
  the dashboard from the parameter `keycloak-admin-password`. It is valid for this run only and is
  never written to the repository.
- **Keycloak imports the realm `coldframe` on start** from
  [`aspire/keycloak/realms/coldframe-realm.json`](../aspire/keycloak/realms/coldframe-realm.json):
  registration and Organizations on, and the confidential client `coldframe-web` for the web app
  (standard flow only, PKCE S256, redirect `http://localhost:5173/.oidc/signin/callback`). Its
  client secret is generated on every start; read it in the dashboard from the parameter
  `coldframe-web-client-secret`. It also holds the public client `coldframe-mobile` for the iOS
  and Android apps (standard flow only, PKCE S256, no secret, redirect
  `com.escendit.coldframe:/signin/callback`, post-logout `com.escendit.coldframe:/signout/callback`).
  The file is copied into the container, so it works under SELinux. A realm that already exists is
  left as it is: if your Keycloak database outlives a run and lacks a client, remove the realm (or
  the database) so the file is imported again.
- **The Server has its own Keycloak client.** The confidential client `coldframe-server` has only a
  service account, with the `realm-management` roles `view-organizations` and `manage-organizations`;
  the Server uses it to create Phase Two Organizations for Sites. Its secret is generated on every
  start; read it in the dashboard from the parameter `coldframe-server-client-secret`. Tokens of
  `coldframe-web` and `coldframe-mobile` carry the audience `coldframe-server`, which the Server
  requires. The realm sets `_providerConfig.orgs.config.createAdminUser` to `false`, so Phase Two
  creates no placeholder `org-admin-*` User per Organization.
- **Keycloak is built, not pulled.** [`aspire/keycloak/Dockerfile`](../aspire/keycloak/Dockerfile)
  compiles the extension from its public source and adds it to the Phase Two image. The realm
  `coldframe` has the fixed ID `coldframe` and already lists the `temporal` event listener, so its
  admin events reach the Server through Temporal.
- **Changes made in Keycloak reach the Server.** Add or remove an Organization member, grant or revoke
  its `owner`, `administrator` or `member` role, rename or delete the Organization, and the Server's
  Temporal workers reconcile the Site within seconds (settings `KeycloakEvents__TargetHost`,
  `KeycloakEvents__Namespace` and `KeycloakEvents__RealmId`, which the AppHost sets). An edit that
  leaves a Site without an Owner is refused: the previous Owners keep Owner, the Server logs an `Error`
  with EventId 3 (`OwnerlessEditRefused`) and journals `site.ownerless-edit-refused`; fix it by giving
  the Organization an `owner` again in Keycloak. See
  [`apps/cs/README.md`](../apps/cs/README.md#reconciliation-from-keycloak).
- **Temporal is a development server.** It keeps its state in memory. The version for deployment
  is decided with the deployment epic.
- **The silo uses ADO.NET clustering and reminders** on the `coldframe` database, in the Orleans
  tables the migration job creates. Its ports are allocated per start, so the stack and the
  integration tests can run at the same time.
- **The Server's Edge API** listens on `http://localhost:5080`. `POST /sites` (header
  `Idempotency-Key`, body `{"name":"Home"}`) creates a Site with the caller as Owner, and
  `GET /sites/{siteId}` reads it. `GET /sites` lists the caller's Active Sites with their Role, oldest
  first; the apps use it to find out whether the user has a Membership and to fill the Site switcher.
  `PATCH /sites/{siteId}` (Owner, body `{"name":"Home garden"}`) renames a Site; the Server sets the
  Keycloak Organization's display name first. Lots live under `/sites/{siteId}/lots`: `GET` lists the
  live Lots in the Server's order (Member), `POST` creates one (Administrator, `Idempotency-Key`),
  and `GET`, `PATCH` and `DELETE /sites/{siteId}/lots/{lotId}` read (a removed Lot too, with
  `removed: true`), rename and remove one (a Lot holding a Node answers 409 `lot-claimed`).
  All need an access token from the `coldframe` realm. The contract is
  [`packages/openapi/coldframe.openapi.json`](../packages/openapi/coldframe.openapi.json); how to add an
  endpoint is described in [`apps/cs/README.md`](../apps/cs/README.md#add-an-endpoint).
- **The Server never changes the schema.** Every table comes from the migration job in
  [`apps/cs/migrations`](../apps/cs/migrations). How to add a migration, an event type, an upcaster or
  a projector is described in [`apps/cs/README.md`](../apps/cs/README.md).

## Run the web app

The web app in [`apps/ts/web`](../apps/ts/web) is a SvelteKit backend-for-frontend. It signs in
through Keycloak on the server side; the browser only ever holds a session cookie. Start the local
stack first, then, from the repository root:

```sh
pnpm install --frozen-lockfile
COLDFRAME_SERVER_URL=http://localhost:5080 \
KEYCLOAK_ISSUER=http://localhost:<keycloak port>/realms/coldframe \
KEYCLOAK_CLIENT_ID=coldframe-web \
KEYCLOAK_CLIENT_SECRET=<coldframe-web-client-secret> \
KEYCLOAK_ALLOW_INSECURE_HTTP=true \
pnpm --filter @coldframe/web dev
```

Open `http://localhost:5173`. Take the Keycloak port from the `http` endpoint of `keycloak` in the
dashboard and the secret from the parameter `coldframe-web-client-secret`; both change on every
start. The port 5173 must stay as it is, because it is the client's registered redirect. Create a
user through *Register* on the Keycloak sign-in page. The variables can also go in
`apps/ts/web/.env`, which git ignores. [`apps/ts/web/README.md`](../apps/ts/web/README.md) lists
every variable.

## Run the Android app

The Android app in [`apps/kt/android`](../apps/kt/android) is a Jetpack Compose shell over the
shared Kotlin core. Point Gradle at the Android SDK once, in `local.properties` in the repository
root (git ignores it), or through `ANDROID_HOME`:

```properties
sdk.dir=/path/to/Android/Sdk
```

The Server URL and Keycloak issuer are fixed at build time (AD-23); users cannot enter them. Pass
them as Gradle properties, or put the same keys in `~/.gradle/gradle.properties`:

```sh
./gradlew :android:installDebug \
  -Pcoldframe.serverUrl=http://10.0.2.2:5080 \
  -Pcoldframe.keycloakIssuer=http://10.0.2.2:<keycloak port>/realms/coldframe
```

`coldframe.keycloakClientId` defaults to `coldframe-mobile`. Unset values fall back to
never-resolvable `.invalid` addresses, so an unconfigured build shows "Can't reach your Coldframe
Server". `10.0.2.2` is the host as seen from the Android emulator; only the debug build allows
plain HTTP, and only to `10.0.2.2` and `localhost`. Release builds trust public certificates only.
Sign-in opens Keycloak in a Custom Tab; create a user through *Register* there.

## Run the iOS app

The iOS app in [`apps/swift/ios`](../apps/swift/ios) is a SwiftUI shell over the same core, which
Gradle builds as the static framework `ColdframeCore`. On macOS, with Xcode 26 and XcodeGen:

```sh
cd apps/swift/ios
xcodegen generate
open Coldframe.xcodeproj
```

Set the build-time configuration in `apps/swift/ios/Config/Coldframe.local.xcconfig` (git ignores
it). `//` starts a comment in an xcconfig, so write URLs with `/$()/`:

```text
COLDFRAME_SERVER_URL = http:/$()/localhost:5080
COLDFRAME_KEYCLOAK_ISSUER = http:/$()/localhost:<keycloak port>/realms/coldframe
```

The build runs `./gradlew :core:embedAndSignAppleFrameworkForXcode` first, so it needs JDK 25 and,
because Gradle configures every project, the Android SDK. Only the Debug configuration allows
plain HTTP to local-network hosts (`NSAllowsLocalNetworking`); Release uses the default App
Transport Security. Sign-in opens Keycloak in `ASWebAuthenticationSession`.

## Run the tests and lints

Run every command from the repository root. CI runs the same checks, see
[`.github/workflows/ci.yml`](../.github/workflows/ci.yml). It adds `--locked` to the Cargo
commands and `--no-restore` or `--no-build` to the .NET commands that follow a restore or a build.

### C#

```sh
dotnet restore --locked-mode
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes
dotnet test
```

`dotnet test` runs two projects. `tests/cs/server.tests` needs no containers: event registry and
upcasters, the replay of the fixture journal, time substitution, the wall-clock ban, and the Edge API
rules (access decisions, request validation, and the check that every endpoint declares one access
rule matching the OpenAPI contract).
`tests/cs/server.integration` starts the same AppHost as above, so it needs Docker or Podman. With
Podman, run `ASPIRE_CONTAINER_RUNTIME=podman dotnet test`. Its journal tests each create a fresh
database on the AppHost's PostgreSQL, migrate it with the job's runner and drop it afterwards. The
Create Site and reconciliation tests run the User and Site grains on such a database with a fake Phase
Two; the Edge API tests and the authorization matrix call the running Server with tokens of Keycloak
Users they create through a test client of their own, and the Keycloak reconciliation tests change
Organizations through the Phase Two API and wait for the Server to follow.

Server code reads the time only from an injected `TimeProvider`. `DateTime.UtcNow` and its relatives
fail the build with RS0030.

### Rust

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
```

Integration tests for a crate live in a test crate of their own, `tests/rs/<crate>`, which is a
member of the workspace. Cargo discovers every file in its `tests/` folder; a new file needs no
registration.

Firmware under `apps/rs` is not part of this workspace. Each firmware crate has its own toolchain
and its own README.

### TypeScript

```sh
pnpm install --frozen-lockfile
pnpm --filter @coldframe/web-e2e exec playwright install --with-deps chromium
pnpm -r lint
pnpm -r typecheck
pnpm -r test
pnpm --filter @coldframe/design-tokens run check
```

The Playwright install is needed once per machine. `pnpm -r test` includes the web app's tests:
`tests/ts/web` holds the unit tests (Vitest, including server-side renders of every component and
a check that no `.svelte` file hard-codes copy), `tests/ts/web.e2e` the end-to-end tests
(Playwright, Chromium). The end-to-end run builds the app and starts three `node build` instances
against a fake OIDC provider in `tests/ts/web.e2e/fixtures`, so it needs no containers. A test for a
UX requirement starts its name with the requirement's id, such as `UX-DR56 …`; a coverage test
fails when an id of the story is named by no test.

The TypeScript API client in [`packages/ts/api-client`](../packages/ts/api-client) is generated from
the OpenAPI contract; after changing `packages/openapi/coldframe.openapi.json`, run
`pnpm --filter @coldframe/api-client generate` and commit `src/schema.ts`. Its tests fail when the
committed schema is stale. The end-to-end fake in `tests/ts/web.e2e/fixtures` also stands in for the
Server's Sites and Lots endpoints. `specs/garden.spec.ts` compares screenshots of Create Site and
the empty Garden, and `specs/site-settings.spec.ts` those of Site settings and Garden with Lots, with
the baselines next to them (Linux, Chromium); after a deliberate visual change, rebuild and rerun
them with `pnpm --filter @coldframe/web build && pnpm --filter @coldframe/web-e2e exec playwright test garden site-settings --update-snapshots` and
commit the new PNGs.

The last command fails when a generated design-token output is stale and prints the recomputed
contrast table. Colours, typography, spacing, radii, Carbon icons and fonts have one source,
[`packages/design-tokens`](../packages/design-tokens); after changing it, run
`pnpm --filter @coldframe/design-tokens run generate` and commit the CSS, TypeScript, Swift
(`packages/swift/design-tokens`) and Kotlin (`packages/kt/design-tokens`) outputs together.

### Kotlin

```sh
./gradlew check
```

`check` compiles, runs the tests and runs ktlint and Android lint. It needs the Android SDK (see
[Run the Android app](#run-the-android-app)). The shared core's tests in `tests/kt/core` run on the
JVM with the network mocked; the Android app's tests in `tests/kt/android` run under Robolectric.
On Linux the iOS targets of the core compile but do not link:

```sh
./gradlew :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64
```

The Android app's snapshot tests (Roborazzi) compare Create Site, Garden (with Lots) and Site settings, in light and
dark at font scale 2, with the PNGs in `tests/kt/android/snapshots`; `check` fails on a difference.
After a deliberate visual change, record new baselines with `./gradlew :android:recordRoborazziDebug`
and commit them. The core's `OpenApiContractTest` fails when an operation, header or property the
hand-written Kotlin API client uses is missing from the OpenAPI contract.

`./gradlew ktlintFormat` fixes what ktlint can fix on its own.

### Swift

On macOS:

```sh
swift build
swift test
swift format lint --strict -r .
```

The SwiftUI views and the tests that render them build only where SwiftUI exists; on Linux the
presentation models and the String Catalog checks still run. On Linux, run the same commands in
the Swift container:

```sh
docker run --rm -v "$PWD":/work -w /work swift:6.3.3 \
  sh -c 'swift build && swift test && swift format lint --strict -r .'
```

With Podman on a system with SELinux, add `--security-opt label=disable`.

### Secrets

CI fails when the history contains something that looks like a secret. To run the same scan locally:

```sh
docker run --rm -v "$PWD":/repo:ro ghcr.io/gitleaks/gitleaks:v8.30.1 \
  git /repo --config /repo/.gitleaks.toml --redact --no-banner
```

With Podman on a system with SELinux, add `--security-opt label=disable`.

## Where things go

```text
apps/       runtimes: firmware, Server, web app, mobile shells
packages/   what the runtimes reference: contracts, shared libraries, generated clients
tests/      all tests, by language: cs/ rs/ kt/ swift/ ts/
aspire/     the local stack and integration-test host
deploy/     Helm charts, Fleet bundles, Compose example
hardware/   schematics, PCB, enclosure
docs/       guides and references
```

- **Tests live under `tests/<language>/`**, in a folder named after the code they test. The only
  exception is Rust unit tests, which stay inline in a `#[cfg(test)]` module. Rust integration
  tests live in the test crate `tests/rs/<crate>`.
- **Write the test first.** Turn each acceptance criterion into a failing test before the code
  that makes it pass.
- **Commits follow [Conventional Commits](https://www.conventionalcommits.org)** with a scope per
  app or package, such as `feat(server): …`.
- **Never commit a secret.** Local credentials are generated by the AppHost.

## Add or update a dependency

| Language | Where the version lives | Afterwards |
| --- | --- | --- |
| C# | `Directory.Packages.props`; a `PackageReference` never carries a version | Run `dotnet restore` and commit the changed `packages.lock.json` files |
| Rust | The crate's `Cargo.toml` | Commit `Cargo.lock` |
| TypeScript | `package.json`, as an exact version | Run `pnpm install` and commit `pnpm-lock.yaml`. The `@escendit` scope is pinned to npmjs in `.npmrc`, and `pnpm-workspace.yaml` adds `base58-js` to `@escendit/sveltekit-session@0.1.0-rc.12`, which forgot to declare it; remove that extension with the next release |
| Kotlin | `gradle/libs.versions.toml` | |
| Containers | `aspire/Coldframe.AppHost/AppHost.cs` and `aspire/keycloak/Dockerfile` | |
| CI actions | `.github/workflows/ci.yml`, as a commit with the release in a comment | |

Orleans, `Microsoft.Orleans.Streaming.NATS` and `NATS.Net` are pinned on purpose. NATS.Net must
stay on 2.x. `keycloak-temporal-extensions` must be verified again whenever it or the Keycloak
image changes.
