# Epic 10 Context: Let others rebuild Coldframe

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Make Coldframe reproducible by someone other than the author, using only the public repository and docs. A maker should be able to build a Node and a Hub, flash them, deploy the full stack on their own home server, build their own mobile apps, and get a Threshold Alert, with every piece tied to one released version. This epic delivers the release pipeline (one SemVer plus firmware binaries), the hardware designs, the adopter build and deploy guides, the Compose reference example, and the firmware learning reference. It closes out the open-source and reproducibility promises and is validated by a real reproduction run.

## Stories

- Story 10.1: Releases with one version and firmware binaries
- Story 10.2: Build a Node and a Hub from the docs
- Story 10.3: Deploy Coldframe at home and build my own apps
- Story 10.4: Firmware learning reference
- Story 10.5: Someone else reproduces Coldframe

## Requirements & Constraints

- **Reproducibility:** another person can build the Node and Hub and run the full stack from the public docs alone. Container images are provided. The supported deployment is single-node RKE2 with Fleet. Compose is a reference example only and must be labelled that way.
- **Open source:** firmware, Server, both apps and the Node/Hub hardware design are public under Apache-2.0. No proprietary vendor libraries are used (for example no Bosch BSEC; the air Sensor reports raw gas resistance). Every dependency must be available from a public registry or repository.
- **Firmware as a learning reference:** the Rust firmware is documented well enough to learn ESP-NOW, BLE provisioning and deep sleep from it.
- **Success criterion:** at least one person other than the author (or the author on a clean machine with fresh hardware and no prior state) reproduces the setup end to end. Every point where they get stuck is filed as an issue and fixed in the docs before the epic closes, and the checklist results are committed to the repo.
- **Test-first where it applies:** link checks, the doc build and documented command snippets are exercised in CI (firmware build only, never flashing). The full reproduction is verified manually against a checklist.
- **Facts the docs must state correctly:** the eFuse key burn is irreversible (with a prominent warning and dev-mode guidance for test boards). Sleep-current budget is about 100 µA average or less. Soil moisture % and battery % are approximate. WPA3-only Wi-Fi is unsupported. A short button press sends a Reading now and a long press enters BLE setup.

## Technical Decisions

- **One version:** one SemVer per release tag `vX.Y.Z` across the monorepo. Every Helm chart's `appVersion` equals that tag. Images go to `ghcr.io/escendit/coldframe/<component>:X.Y.Z`, multi-arch where supported (the image publishing itself comes from Epic 2).
- **Firmware release:** Hub and Node binaries are attached to each release with checksums. They are always release builds. Release builds refuse to compile with dev mode on. Firmware is flashed over USB with `espflash` in V1; there is no OTA.
- **Release notes** are grouped by Conventional Commit type (commit scopes are per app or package).
- **Wire compatibility gate:** a release that bumps a wire-protocol major must fail unless the Server still accepts the previous major. Every frame and envelope carries `protocol_version`.
- **Mobile:** no store binaries are published. Adopters build the apps from source with their own APNs key and FCM project. The Server URL and Keycloak issuer are build-time configuration, and the apps have no field to change them.
- **Deployment envelope the guide must follow:** single-node RKE2 with Fleet pulling the charts in `deploy/`. TLS uses public Let's Encrypt certificates via DNS-01 for a real domain, with split DNS to the LAN ingress (a private CA is only a documented fallback). Secrets are created out of band from the fixed list in `deploy/SECRETS.md`. Keycloak runs on Phase Two, and its realm setup includes the invitation email template with the home-network hint. CNPG backups go to off-node S3.
- **Repo layout:** `hardware/` holds schematics, BOM, wiring, enclosure and sealing notes. `docs/` holds the adopter build guide (`docs/build/`), the deploy guide (`docs/deploy/`), the firmware reference (`docs/firmware/`) and the reproduction checklist. `deploy/compose/` holds the Compose example.
- **Firmware reference topics, with pointers to code:** ESP-NOW transport and channel following, the custom BLE setup protocol with its proof-of-possession code, the eFuse root and HKDF purpose-key hierarchy, ChaCha20-Poly1305 frame sealing with counters and the replay window, deep sleep and the power budget, button presses, and the findings from the Hub radio-coexistence spike (`docs/spikes/hub-radio-coexistence.md`).
- **Rust docs:** `cargo doc` over `packages/rs/*` must document every public item and finish with no warnings in CI.

## Cross-Story Dependencies

- Epic 10 comes last. It documents and packages what Epics 1–9 built. It depends especially on Epic 2 (images, charts, Fleet bundles, `deploy/SECRETS.md`, ops runbooks, which the deploy guide links to), Epics 3–4 (Hub/Node firmware, dev mode, eFuse enrolment) and Epic 9 (the Phase Two invitation template).
- 10.1's release notes point to the build-your-own-apps guide in 10.3. 10.2 flashes the release binaries produced by 10.1.
- 10.5 requires 10.1–10.4 to be complete. It follows 10.2 and 10.3 end to end, and it may feed doc fixes back into them.
