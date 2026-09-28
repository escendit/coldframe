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
- **Keycloak is built, not pulled.** [`aspire/keycloak/Dockerfile`](../aspire/keycloak/Dockerfile)
  compiles the extension from its public source and adds it to the Phase Two image. To use the
  listener in a realm, add `temporal` under *Realm settings → Events → Event listeners*.
- **Temporal is a development server.** It keeps its state in memory. The version for deployment
  is decided with the deployment epic.
- **The silo uses ADO.NET clustering and reminders** on the `coldframe` database, in the Orleans
  tables the migration job creates. Its ports are allocated per start, so the stack and the
  integration tests can run at the same time.
- **The Server never changes the schema.** Every table comes from the migration job in
  [`apps/cs/migrations`](../apps/cs/migrations). How to add a migration, an event type, an upcaster or
  a projector is described in [`apps/cs/README.md`](../apps/cs/README.md).

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
upcasters, the replay of the fixture journal, time substitution and the wall-clock ban.
`tests/cs/server.integration` starts the same AppHost as above, so it needs Docker or Podman. With
Podman, run `ASPIRE_CONTAINER_RUNTIME=podman dotnet test`. Its journal tests each create a fresh
database on the AppHost's PostgreSQL, migrate it with the job's runner and drop it afterwards.

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
pnpm -r lint
pnpm -r typecheck
pnpm -r test
pnpm --filter @coldframe/design-tokens run check
```

The last command fails when a generated design-token output is stale and prints the recomputed
contrast table. Colours, typography, spacing, radii, Carbon icons and fonts have one source,
[`packages/design-tokens`](../packages/design-tokens); after changing it, run
`pnpm --filter @coldframe/design-tokens run generate` and commit the CSS, TypeScript, Swift
(`packages/swift/design-tokens`) and Kotlin (`packages/kt/design-tokens`) outputs together.

### Kotlin

```sh
./gradlew check
```

`check` compiles, runs the tests and runs ktlint. `./gradlew ktlintFormat` fixes what ktlint can
fix on its own.

### Swift

On macOS:

```sh
swift build
swift test
swift format lint --strict -r .
```

On Linux, run the same commands in the Swift container:

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
| TypeScript | `package.json`, as an exact version | Run `pnpm install` and commit `pnpm-lock.yaml` |
| Kotlin | `gradle/libs.versions.toml` | |
| Containers | `aspire/Coldframe.AppHost/AppHost.cs` and `aspire/keycloak/Dockerfile` | |
| CI actions | `.github/workflows/ci.yml`, as a commit with the release in a comment | |

Orleans, `Microsoft.Orleans.Streaming.NATS` and `NATS.Net` are pinned on purpose. NATS.Net must
stay on 2.x. `keycloak-temporal-extensions` must be verified again whenever it or the Keycloak
image changes.
