# Reproduction checklist: someone else builds Coldframe (Story 10.5)

This checklist is the test of Coldframe's promise that it can be reproduced from the public
repository alone (success criterion SM-5). It walks the [build guide](../build/README.md) and the
[deploy guide](../deploy/README.md) end to end, from the parts to a Threshold Alert on your phone.
It does not repeat their commands: **the guides are what is under test**. Each part names the guide
section that does the work and lists what you must observe when it is done.

Who may run it:

- **A person other than the author**, using only this public repository and its docs; or
- **the author on a clean machine with fresh hardware and no prior state**: no cached toolchain, no
  existing cluster, no Keycloak realm, no enrolled boards, no app build or account left over from
  earlier work. Part A records that attestation; a run without it does not count.

Nobody helps you beyond what is written in the repository. When a guide is unclear, wrong or
incomplete, that is a **stuck point**: do not ask the author, file it
([When you get stuck](#when-you-get-stuck)) and work around it only if the guide's own text lets
you. Every stuck point is fixed in the docs before the story closes.

At the end you write a result record from [`results/TEMPLATE.md`](results/TEMPLATE.md) and commit
it under [`results/`](results/README.md).

## What you need

- **A release tag** `vX.Y.Z` of this repository with its GitHub Release (firmware binaries and
  `SHA256SUMS`): [`docs/operations/release.md`](../operations/release.md). Use the same tag for
  everything below.
- **The parts for one Node and one Hub** from the [bill of materials](../../hardware/bom.md), plus
  USB cables, the soil the probe goes into, and the tools to assemble the Node.
- **A home server** that runs all the time, as listed in the deploy guide's
  [Before you start](../deploy/README.md#before-you-start).
- **A domain** whose DNS zone is hosted at Cloudflare, and a local DNS resolver you control.
- **An SMTP account** that can send mail from an address of yours.
- **An S3 bucket** off the home server for the database backups.
- **One or two phones**: an iPhone with a Mac, Xcode and an Apple Developer Program membership,
  and/or an Android phone with a Firebase project
  ([what the apps need](../build/apps.md#what-you-need)).
- **A 2.4 GHz Wi-Fi network** with WPA2-Personal or mixed WPA2/WPA3. WPA3-only is not supported
  ([`flash.md`, Wi-Fi](../build/flash.md#wi-fi)).
- **A clean machine** to run the commands and builds from, with nothing of Coldframe on it yet.
- A notebook (paper or a local file outside the repository) for the time each part takes and every
  stuck point.

Keep every key, token, password and setup code out of the repository, out of issues and out of the
result record.

## A. Start clean

- [ ] You are not the author, **or** you are the author and every item below is true. Write which in
      the result record.

The remaining items are required for an author run. A non-author ticks each one only where it is
true and writes n/a in the result record otherwise.

- [ ] The machine you run the commands and builds from has no esp toolchain, `espflash`, Rust
      target cache, Gradle or Xcode build cache for Coldframe, and no kubeconfig for a Coldframe
      cluster.
- [ ] The home server has no RKE2, Fleet or Coldframe install on it.
- [ ] The Hub and Node boards are fresh: never flashed with Coldframe (Part D checks the key
      blocks).
- [ ] No Keycloak realm, account, Site or Device of an earlier Coldframe install exists anywhere you
      will use, and no Coldframe app is installed on the phones.
- [ ] No Firebase project, APNs key, Cloudflare API token or S3 bucket made for an earlier Coldframe
      install is reused.
- [ ] You have the repository from its public URL, checked out at the release tag of Part B, and no
      other notes than the docs in it.

## B. The release

Follow [`docs/operations/release.md`, What gets published](../operations/release.md#what-gets-published)
and [Verify a download](../operations/release.md#verify-a-download).

- [ ] The GitHub Release for `vX.Y.Z` exists and carries `coldframe-hub-X.Y.Z.bin`,
      `coldframe-node-X.Y.Z.bin` and `SHA256SUMS`.
- [ ] The download checks: with every Release asset downloaded next to `SHA256SUMS`, every line of
      the checksum check says `OK`.
- [ ] The release notes link the build-your-own-apps guide.
- [ ] Your clone is at that tag; the deploy guide, the firmware and the apps are all built from it.

## C. Build the Node and the Hub

Follow the [build guide](../build/README.md), steps
[1. Get the parts](../build/README.md#1-get-the-parts) and
[2. Assemble](../build/README.md#2-assemble).

- [ ] Every part you bought matches a line of the [BOM](../../hardware/bom.md), or a substitute meets
      that line's requirement.
- [ ] The Node is wired exactly as in [`hardware/node.md`](../../hardware/node.md), without guessing a
      connection the page does not state.
- [ ] The Node sits in its enclosure with the soil probe sealed as in
      [`hardware/enclosure.md`](../../hardware/enclosure.md).
- [ ] The Hub is a dev board on USB power, placed as in [`hardware/hub.md`](../../hardware/hub.md).
- [ ] The panel placement follows [power.md](../build/power.md#panel-placement).

## D. Flash the Hub and the Node

Follow [3. Install the toolchain](../build/README.md#3-install-the-toolchain),
[4. Flash the Hub](../build/README.md#4-flash-the-hub) and
[5. Flash the Node](../build/README.md#5-flash-the-node), through
[`flash.md`](../build/flash.md). **Read its eFuse warning first**: the release binaries burn an
eFuse key on the first boot, and that cannot be undone. Only boards meant to stay a Hub or a Node
get them.

- [ ] The toolchain reports the versions [`flash.md`, Toolchain](../build/flash.md#toolchain) pins
      (at least `espflash` for the release binaries).
- [ ] [The key-purpose check](../build/flash.md#before-you-flash-check-the-key-purposes) shows at
      least one unused `USER` key block on each board before you flash it.
- [ ] The Hub's [first boot](../build/flash.md#first-boot-and-the-setup-code) logs its version
      `X.Y.Z`, `source=burned KEYn` once, a setup code, and that it advertises its BLE setup
      service. A reboot logs `source=existing KEYn` with the same Device ID.
- [ ] The Node's first wake logs its version and its identity, then measures and sleeps. Its first
      wake logs `source=burned KEYn` once; a later wake logs `source=existing KEYn` with the same
      Device ID.
- [ ] A long press (3 s) of the Node's setup button
      ([the buttons](../build/flash.md#the-buttons)) logs `wake plan=setup`, a setup code and a
      180 s setup window (`window_ms=180000`).
- [ ] Each setup code is on a sticker on its Device, and nowhere else.

## E. Deploy the Server at home

Follow the [deploy guide](../deploy/README.md), sections
[1. Domain and DNS API token](../deploy/README.md#1-domain-and-dns-api-token) to
[6. First sign-in](../deploy/README.md#6-first-sign-in), in order: single-node RKE2 with Fleet,
Let's Encrypt certificates through DNS-01, split DNS. The Compose example does not count.

- [ ] RKE2 is installed and `kubectl` shows one node, Ready.
- [ ] Fleet is installed, and every required Secret of section 3 exists before the GitRepo is
      applied (the optional `coldframe-push` may follow in Part F).
- [ ] The `coldframe-realm` ConfigMap is written with `prepare-realm.sh` for `https://<domain>`
      ([section 4](../deploy/README.md#4-keycloak-realm-and-phase-two)), and Keycloak imports the
      `coldframe` realm on its first start.
- [ ] The GitRepo points at your release tag, and every bundle becomes Ready.
- [ ] From a laptop on the home network the three hosts (`<domain>`, `api.<domain>`,
      `auth.<domain>`) resolve to the home server's LAN address and serve a Let's Encrypt
      certificate with no warning.
- [ ] You register in the web app, the verification email arrives through your SMTP account, and
      you create a Site.
- [ ] Keycloak's temporary bootstrap admin is replaced by a permanent admin in the master realm and
      deleted ([section 6](../deploy/README.md#6-first-sign-in)), and the `coldframe-keycloak-admin`
      Secret is kept.
- [ ] Every item of the
      [verification checklist on the home server](../operations/install.md#verification-checklist-on-the-home-server)
      is ticked, including a backup in your S3 bucket.

## F. Build your own apps

Follow [`docs/build/apps.md`](../build/apps.md) from the same release tag:
[Android](../build/apps.md#android) and/or [iOS](../build/apps.md#ios), with your own ids, then
[Server push](../build/apps.md#server-push).

- [ ] Each app builds with your Server URL and Keycloak issuer, and installs on your phone. No key,
      team ID, keystore or `google-services.json` was added to the repository (`git status` is
      clean apart from the git-ignored files the guide names).
- [ ] Each app signs in on the home Wi-Fi and shows the Site of Part E, with no Unreachable notice.
- [ ] The Server's log has `PushChannelConfigured` for every provider you set up.
- [ ] Each app asks for notification permission, and you allow it.

## G. Add the Hub and the Node, calibrate

Follow [6. Add them from the app](../build/README.md#6-add-them-from-the-app).

- [ ] **Add a Hub**: the app finds the unprovisioned Hub over BLE, takes its setup code from the
      sticker, and sends it your 2.4 GHz WPA2 (or mixed WPA2/WPA3) network and your Server's
      address. The app reports the Hub added to the Site, and its serial output no longer prints a
      setup code on boot.
- [ ] Create a Lot for the Node if the Site has none.
- [ ] **Add a Node**: long-press its setup button (3 s), pick the Lot, and enter its setup code
      within the 180 s setup window. The Node is listed on that Lot.
- [ ] A short press of the Node's button sends a Reading now, and the Lot shows it.
- [ ] Put the probe in the soil it stays in and leave it **2–3 days to settle**
      ([why](../build/power.md#soil-moisture)). Readings keep arriving the whole time.
- [ ] Calibrate the soil Sensor in the app (a dry point and a wet point). Afterwards the Lot shows
      soil moisture as a % (a multiple of 5) and the Sensor no longer asks for Calibration.
- [ ] The Lot's low Threshold is the default **30 %** unless you changed it; note the value you use.

## H. A Threshold Alert inside the Notification Window

The Alert flow is described in the
[push checklist, Part D](../bench/push-checklist.md#d-an-alert-on-both-phones). Do this inside your
Notification Window (My notifications; the default is **07:00 to 22:00** in your time zone), with
the app in the background.

- [ ] Let the Lot's soil read below its low Threshold for **three consecutive Readings**: take the
      probe out of the soil (a short press sends each Reading now).
- [ ] Within a minute of the third Reading each phone shows **one** notification titled
      "‹Lot› needs water", with the body "~‹value› % in the soil, your low is ‹low› %."
- [ ] Tapping the notification opens Lot detail of that Lot.
- [ ] Put the probe back and let the Lot recover (three Readings within the Thresholds): the Lot
      shows within its Thresholds again (the Alert closes), and no new notification arrives.

## When you get stuck

A stuck point is any place where the docs alone did not get you to the next check: a missing step,
a wrong command or value, a link that leads nowhere useful, an unexplained error, or a guess you had
to make.

1. Open a new issue on GitHub and choose the **Reproduction stuck point** form. Name the part
   (A–H), the guide page and section, what you did, what you expected and what happened. Paste no
   secret, setup code, token or private domain credentials.
2. Write the issue number in your notes, next to the part.
3. Carry on only if you can without the author's help; otherwise stop the run there and record the
   result as failed at that part.

Each stuck point is fixed by a pull request to the docs. The story stays open until every issue
listed in the result record is fixed. After a doc fix merges, rerun the affected parts following
the fixed docs on `main`, with the same release artifacts and the boards already flashed, and note
the rerun in the same record. A fix in the firmware, the Server, the charts or the apps ships in a
new release tag; the run then continues from the first affected part on that tag.

## Record the result

Copy [`results/TEMPLATE.md`](results/TEMPLATE.md) to `docs/reproduce/results/YYYY-MM-DD-<tester>.md`,
fill it in, and open a pull request with it ([`results/README.md`](results/README.md) has the
rules). Fill it in truthfully, including the parts that failed.

## Result

Pass: parts A to H all ticked, with the attestation of Part A, on one release tag, and every
stuck-point issue raised during the run fixed in the docs. A run where A to H passed but listed
issues are still open is **pending doc fixes**; it becomes a pass once every listed issue's fix is
merged. Anything else is a failed run of Story 10.5, and SM-5 does not hold until a later run
passes.
