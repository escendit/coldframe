### DW-1: The architecture Stack pins Temporal Server 1.31.3, but no public container image exists for that version; the local stack uses the Temporal CLI development server instead.
origin: spec-deferred d24a3b6afbf1
location: _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md (Stack table)
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: low
reason: Docker Hub temporalio/server lists 1.31.0, 1.31.1, 1.31.2 and 1.32.0 only; temporalio/auto-setup stops at 1.29.7 (checked 2026-09-28).
status: open

### DW-2: The Escendit hosting packages extend the concrete HostApplicationBuilder only, so an ASP.NET Core host cannot call AddServiceDefaults() or any Orleans extension of Escendit.Extensions.Hosting.Orleans;
origin: spec-deferred bc3a356a215e
location: apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: The XML documentation of Escendit.Extensions.Hosting.ServiceDefaults 0.1.0-rc.4 lists AddServiceDefaults(HostApplicationBuilder, ...) and no overload for IHostApplicationBuilder or WebApplicationBuilder. Story 1.2 meets the same limit when it adds AdoNet clustering and NATS streams. Needs an upstream change (target IHostApplicationBuilder) or a decision to keep the shim.
status: open

### DW-3: No test asserts that the server exports telemetry when OTEL_EXPORTER_OTLP_ENDPOINT is set.
origin: spec-deferred 5f08a759f34d
location: apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: low
reason: The integration run executes the branch but asserts nothing about telemetry. An assertion needs an OTLP collector in the test host.
status: open

### DW-4: The health test may fail on a slow runner when the server process is running but does not listen within the retry budget of the HTTP resilience handler.
origin: spec-deferred a7f34e0af370
location: tests/cs/server.integration/ServerHealthTests.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
reason: Not observed in any local run. To settle it, measure the time from the Running state to the first accepted connection on a GitHub runner and compare it with the retry budget of the standard resilience handler.
status: open

### DW-5: The CI jobs are not required status checks on main, so a failing job does not block a merge.
origin: spec-deferred eb356faf4c50
location: .github/workflows/ci.yml
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: The GitHub API reports no branch protection on main and a ruleset with deletion and non_fast_forward only. This is a repository setting; it is listed under operator_actions.
status: open

### DW-6: The CI workflow has never run on GitHub; the macOS Swift job and the Docker-based .NET and secrets jobs are verified only by running their commands locally.
origin: spec-deferred 7c23aad3bce0
location: .github/workflows/ci.yml
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: This run may not push or open a pull request. It is listed under operator_actions.
status: open

### DW-7: AD-21 asks for journal snapshots on a fixed event interval; this story has no acceptance criterion for them and no grain yet has a long stream, so the CustomStorage read replays the full stream.
origin: spec-deferred a59fe982da88
location: apps/cs/server/Journal/
source_spec: `spec-1-2-event-journal-migrations-and-projection-pipeline.md`
severity: low
reason: Story 1.2 acceptance criteria in epics.md (lines 527-561) name append, outbox, projectors, polling, time and replay, not snapshots.
status: open

### DW-8: Escendit.Orleans.Migrations.Cluster.PostgreSQL 10.3.1-rc.1 is published; the architecture and the story pin 10.3.1-rc.0, which this story keeps.
origin: spec-deferred 9c186f8991dd
location: Directory.Packages.props
source_spec: `spec-1-2-event-journal-migrations-and-projection-pipeline.md`
severity: low
reason: nuget.org flat container index lists 10.3.1-rc.0 and 10.3.1-rc.1 (checked 2026-09-28).
status: open

### DW-9: DESIGN.md sets hero-value, tile-value and tile-value-web to Ubuntu Condensed weight 300, but Ubuntu Condensed exists only in 400, so those roles render at 400.
origin: spec-deferred c0e9a91eb7ae
location: packages/design-tokens/tokens/tokens.json (hero-value, tile-value, tile-value-web)
source_spec: `spec-1-3-design-tokens-and-themes.md`
severity: low
reason: google/fonts ufl/ubuntucondensed ships only UbuntuCondensed-Regular.ttf; generated fonts.css has no Ubuntu Condensed 300 face. The token copies DESIGN.md faithfully; the design needs a decision (use 400, or Ubuntu Light for values).
status: open

### DW-10: No test drives a real authorization-code exchange between apps/ts/web and the coldframe realm in Keycloak; e2e tests use a fake OIDC provider and the realm is checked only as configuration.
origin: spec-deferred 7ffd886da898
location: aspire/keycloak/realms/coldframe-realm.json, tests/ts/web.e2e/fixtures/fake-idp.ts
source_spec: `spec-1-4-sign-in-on-the-web.md`
reason: Unverified (maybe-false). KeycloakTests.cs asserts the discovery document and the coldframe-web client settings; every browser test targets tests/ts/web.e2e/fixtures/fake-idp.ts. To settle it, sign in to the web app against the Aspire stack's Keycloak (docs/quickstart.md "Run the web app") with a registered user, or add an AppHost-hosted e2e run.
status: open

### DW-11: The CI workflow, including the new Playwright install and report-upload steps, has not run on GitHub; it is verified only by running the same commands locally.
origin: spec-deferred 9a6aaa58a7b5
location: .github/workflows/ci.yml
source_spec: `spec-1-4-sign-in-on-the-web.md`
severity: low
reason: Pre-existing and already tracked as DW-6 (spec-1-1). This run does not push.
status: open

### DW-12: The UX-DR92 unreachable notice says "Check that this phone is on your home Wi-Fi." on the web too; EXPERIENCE.md has no web variant.
origin: spec-deferred 1ad0b8ebf510
location: apps/ts/web/src/lib/i18n/en.json
source_spec: `spec-1-4-sign-in-on-the-web.md`
severity: low
reason: EXPERIENCE.md lines 155-164 give one copy for all platforms; the catalogue uses it verbatim. Changing it needs a UX decision in EXPERIENCE.md, which this story may not edit.
status: open
