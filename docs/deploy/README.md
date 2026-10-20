# Deploy Coldframe at home

This guide takes you from a domain and a spare machine to Coldframe running on your home network,
signed in from your phone. The supported deployment is one RKE2 server on which Fleet installs the
whole stack from this repository at a release tag, with public Let's Encrypt certificates and
split DNS. The [Compose example](../../deploy/compose/README.md) is a reference only and is not
supported.

The guide fixes the order and the decisions, and adds the steps no runbook has. The commands that
already exist stay in the operator runbooks, which remain the source of truth; each section links
the part to follow:

- [`docs/operations/install.md`](../operations/install.md): Fleet, the Secrets, the site values,
  the GitRepo, upgrades and the verification checklist;
- [`docs/operations/split-dns.md`](../operations/split-dns.md): the DNS-01 token, the split DNS
  records and the resolvers;
- [`deploy/SECRETS.md`](../../deploy/SECRETS.md): every Secret and how to create it;
- [`docs/operations/restore.md`](../operations/restore.md): restoring the database from the
  off-node backups.

Work through the six sections in order:

1. [Domain and DNS API token](#1-domain-and-dns-api-token)
2. [RKE2 and Fleet](#2-rke2-and-fleet)
3. [Secrets](#3-secrets)
4. [Keycloak realm and Phase Two](#4-keycloak-realm-and-phase-two)
5. [Split DNS](#5-split-dns)
6. [First sign-in](#6-first-sign-in)

## Before you start

- **A home server** that runs all the time: one amd64 or arm64 machine (the images are published
  for both) with a fixed LAN address reserved in the router's DHCP, for example `192.168.1.10`, a
  64-bit Linux that RKE2 supports, at least 4 GB of RAM (8 GB leaves the stack room) and an SSD for
  the database. RKE2's own requirements:
  [docs.rke2.io/install/requirements](https://docs.rke2.io/install/requirements).
- **A release tag** `vX.Y.Z` of this repository. Everything is tied to it: the charts Fleet
  installs, the images, the firmware you flash and the apps you build
  ([`docs/operations/release.md`](../operations/release.md)). Check out that tag on the machine you
  run the commands from: `git checkout vX.Y.Z`.
- **A local DNS resolver** you control (the router's host overrides, Pi-hole, AdGuard Home,
  Unbound, dnsmasq), handed out by DHCP to every device ([`split-dns.md`](../operations/split-dns.md#what-you-need)).
- **An SMTP account** that can send mail from an address of yours: registration needs a verified
  email, and invitations are emailed.
- **On the machine you run the commands from**: `git`, `kubectl`, `helm`, `curl`, `sha256sum`,
  `jq`, `openssl` and, for the backup check, the `aws` CLI.

What costs money: the domain (yearly), the S3 bucket for the backups (by stored size), the SMTP
service if yours is not free, and, for iPhones, the Apple Developer Program (yearly). Cloudflare's
free plan and Firebase Cloud Messaging cost nothing.

## 1. Domain and DNS API token

Coldframe serves HTTPS only, with Let's Encrypt certificates obtained through the DNS-01 challenge:
Let's Encrypt never connects to your network, and nothing listens on port 80.

1. Choose a **dedicated subdomain** of a domain you own as Coldframe's domain, for example
   `coldframe.example.org` (written `<domain>` below). Prefer it over the apex: the LAN record for
   the web host would hide any public site at the apex.
2. Host the domain's **DNS zone at Cloudflare** (the free plan is enough). Cloudflare is the only
   DNS provider the `ingress` chart supports.
3. Coldframe uses three hosts: `<domain>` (the web app), `api.<domain>` (the Server) and
   `auth.<domain>` (Keycloak). Do not create public records for them; section 5 adds them to your
   LAN resolver only.
4. Create a Cloudflare **API token** with the permissions **Zone → DNS → Edit** and
   **Zone → Zone → Read**, limited to your zone:
   [`split-dns.md`, step 1](../operations/split-dns.md#1-the-dns-01-token). Keep it in a password
   manager for now; it becomes the Secret `coldframe-dns01` in section 3.

## 2. RKE2 and Fleet

**The node must use your LAN resolver.** The Server and the web app validate tokens against
`https://auth.<domain>` from inside the cluster, so the node, and CoreDNS through it, must resolve
the three hosts to the LAN address. Configure the node's DNS to be the LAN resolver of section 5
now (in its network configuration, without a public fallback server); section 5 adds the records
and checks it ([`split-dns.md`](../operations/split-dns.md#the-node-and-the-cluster-must-use-this-resolver)).

Install RKE2 v1.36.4+rke2r1 as a single server on the home server:

```sh
curl -sfL https://get.rke2.io | sudo INSTALL_RKE2_VERSION=v1.36.4+rke2r1 sh -
sudo systemctl enable --now rke2-server.service
systemctl status rke2-server --no-pager  # active (running); the first start takes a few minutes
```

RKE2 writes the cluster-admin kubeconfig to `/etc/rancher/rke2/rke2.yaml` and puts `kubectl` in
`/var/lib/rancher/rke2/bin`. Copy the kubeconfig to the machine you run the commands from
(replace `127.0.0.1` in it with the node's LAN address when that is another machine), and check
the node:

```sh
export PATH="$PATH:/var/lib/rancher/rke2/bin"   # on the node: RKE2's kubectl
mkdir -p ~/.kube
sudo cat /etc/rancher/rke2/rke2.yaml > ~/.kube/config && chmod 600 ~/.kube/config
kubectl get nodes                        # one node, Ready
```

Phones and browsers reach Coldframe on TCP port 443 of the node: allow it inbound in the node's
firewall (with firewalld on Fedora or RHEL, `sudo firewall-cmd --permanent --add-port=443/tcp`
and `sudo firewall-cmd --reload`), and follow RKE2's firewall guidance in
[docs.rke2.io/install/requirements](https://docs.rke2.io/install/requirements).

RKE2 brings its own Traefik, which is Coldframe's ingress. Do not apply
`deploy/rke2/rke2-traefik-config.yaml` by hand: the `rke2-traefik` bundle does
([`deploy/rke2/README.md`](../../deploy/rke2/README.md)).

Then install Fleet from the pinned, checksummed charts:
[`install.md`, step 1](../operations/install.md#1-install-fleet). The rest of the prerequisites
are in [`install.md`](../operations/install.md#prerequisites); the S3 bucket for the backups is
needed in section 3.

## 3. Secrets

Every credential is a Kubernetes Secret you create once, in the namespace `coldframe`, before
Fleet installs anything. The charts never create one, and nothing secret goes into Git. Create
the namespace first, since the Secret commands only name it:

```sh
kubectl create namespace coldframe
```

Then follow [`deploy/SECRETS.md`, Creating the Secrets](../../deploy/SECRETS.md#creating-the-secrets), which
creates:

- `coldframe-db-coldframe`, `coldframe-db-temporal`, `coldframe-db-keycloak`: the database roles
  (labelled `cnpg.io/reload=true`);
- `coldframe-keycloak-admin`: Keycloak's temporary bootstrap admin (keep it after section 6);
- `coldframe-oidc-clients`: the secrets of the `coldframe-web` and `coldframe-server` clients;
- `coldframe-smtp`: your SMTP account, used by Keycloak for verification and invitation emails;
- `coldframe-dns01`: the Cloudflare token of section 1;
- `coldframe-enrolment-key` and `coldframe-device-kek`: Device enrolment. **Record the KEK before
  you create the Secret**, apart from the backups: without it no enrolled Device works, and a
  restore needs the same value;
- `coldframe-backup-s3`: the key of the S3 bucket off the node;
- `coldframe-push`, **optional**: your APNs key and Firebase service account, only for push
  notifications to the apps you build ([`docs/build/apps.md`](../build/apps.md#server-push)).

The order and what a missing Secret does: [`install.md`, step 2](../operations/install.md#2-secrets-and-the-realm).

## 4. Keycloak realm and Phase Two

Keycloak needs the `coldframe` realm with the redirect URIs of your web host. A wrong redirect URI
only shows up as a Keycloak error at the first sign-in, so write the realm with the script rather
than by hand. From the repository root, at the release tag:

```sh
deploy/realm/prepare-realm.sh --web-origin https://<domain> --out coldframe-realm.json
kubectl -n coldframe create configmap coldframe-realm --from-file=coldframe-realm.json=coldframe-realm.json
```

[`prepare-realm.sh`](../../deploy/realm/prepare-realm.sh) (needs `jq`) takes the realm of the local
stack, [`aspire/keycloak/realms/coldframe-realm.json`](../../aspire/keycloak/realms/coldframe-realm.json),
and sets the web client's sign-in and sign-out callbacks to `https://<domain>/.oidc/signin/callback`
and `https://<domain>/.oidc/signout/callback`. It leaves everything else as it is: the `${COLDFRAME_*}`
placeholders, which Keycloak fills from `coldframe-oidc-clients` and `coldframe-smtp`, and the mobile
client with its `com.escendit.coldframe` redirect scheme ([apps guide](../build/apps.md#choose-your-ids)).
It refuses an `http://` origin.

To replace the ConfigMap, for example after a wrong `--web-origin`, before Keycloak's first start
(`create` fails with AlreadyExists when it exists):

```sh
kubectl -n coldframe create configmap coldframe-realm --from-file=coldframe-realm.json=coldframe-realm.json \
  --dry-run=client -o yaml | kubectl apply -f -
```

After the first start the ConfigMap no longer matters: the realm is never re-imported.

Keycloak runs on Phase Two, and the realm file turns on what Coldframe uses of it:

- **Organizations**: each Site is a Phase Two organization; the Server manages them through its
  service account.
- **The `coldframe` email theme**, shipped in the Keycloak image, with the home-network hint in
  every invitation: "Open the link at home, on the garden's Wi-Fi: accepting needs the home network."
- **The Invitation required action**, which shows an invitee the invitation after they sign in.
- **Verify email**: Phase Two offers an invitation only to a verified address.

The SMTP server comes from `coldframe-smtp` when the realm is imported, and **the realm is imported
once only**, on Keycloak's first start. Later changes, including a new SMTP server or client secret,
are made in the Keycloak admin console ([`install.md`, step 2](../operations/install.md#2-secrets-and-the-realm)).

Then write the site values, the one ConfigMap with your domain, S3 bucket and ACME email:
[`install.md`, step 3](../operations/install.md#3-the-site-values). Its `keycloak.realmImport.configMap`
is `coldframe-realm`, the ConfigMap above.

## 5. Split DNS

On your LAN resolver, add one A record per host, each pointing to the home server's LAN address:
[`split-dns.md`, step 5](../operations/split-dns.md#5-split-dns-records). Use host records, not a
local zone for the whole domain, so the `_acme-challenge` records stay public.

Check that the node and the cluster resolve the hosts to the LAN address
([`split-dns.md`](../operations/split-dns.md#the-node-and-the-cluster-must-use-this-resolver)), and
that phones and browsers do not bypass the LAN resolver with Private DNS or DNS over HTTPS
([`split-dns.md`](../operations/split-dns.md#phones-must-not-bypass-the-lan-resolver)).

### Install the stack

With the records in place, set `spec.revision` of the GitRepo to your release tag and apply it, then
watch the bundles become Ready in order ([`install.md`, steps 4 and 5](../operations/install.md#4-apply-the-gitrepo)).
The certificate is issued within a few minutes. Then verify DNS and TLS from a laptop on the home
network: [`split-dns.md`, step 7](../operations/split-dns.md#7-verify).

A private CA is only an unsupported fallback for when DNS-01 with a public domain is impossible
([`split-dns.md`](../operations/split-dns.md#fallback-a-private-ca-unsupported)); the Hub and the
apps trust public roots only.

## 6. First sign-in

1. On a phone or laptop **on the home Wi-Fi**, open `https://<domain>`. There must be no
   certificate warning.
2. Choose **Register**, and create your account.
3. **Verify your email**: Keycloak sends the link through `coldframe-smtp`. Without working SMTP
   no verification email arrives; check the Keycloak log
   (`kubectl -n coldframe logs deployment/keycloak`) and the Secret.
4. **Create a Site.** Whoever creates a Site is its Owner, and can invite others by email.

Then replace Keycloak's temporary bootstrap admin: sign in to `https://auth.<domain>/admin` with
the `coldframe-keycloak-admin` credentials, create a permanent admin in the master realm and
delete the temporary one. Keep the Secret: the Keycloak pod does not start without it
([`deploy/SECRETS.md`](../../deploy/SECRETS.md#creating-the-secrets), after the commands).

Finally tick the [verification checklist](../operations/install.md#verification-checklist-on-the-home-server):
bundles and pods, HTTPS on the three hosts, port 80 closed, sign-in from a phone and a browser,
restart durability and a backup in S3.

## Afterwards

- **Phones**: build the iOS and Android apps for your domain, with your own push credentials:
  [`docs/build/apps.md`](../build/apps.md). Hubs and Nodes: [the build guide](../build/README.md).
- **Upgrades** are a new release tag in the GitRepo; read the release notes first
  ([`install.md`, Upgrades](../operations/install.md#upgrades)). Rebuild the apps from the same tag.
- **Verification**: tick the checklist again after every upgrade.
- **Restore**: when the database is lost or damaged, restore it from the S3 backups with the
  same `coldframe-device-kek` ([`restore.md`](../operations/restore.md)).
