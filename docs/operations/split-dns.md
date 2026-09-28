# TLS with public certificates on the home network (split DNS)

Coldframe serves the web app, the Server API and Keycloak over HTTPS only, with certificates from
Let's Encrypt that every phone, browser and Hub trusts through the public roots. Nothing on the
home network accepts traffic from the internet, and nothing serves port 80 (Story 2.4, AD-13,
NFR1, NFR10). This runbook sets that up on the reference single-node RKE2 server:

- **Certificates** come from Let's Encrypt through the **DNS-01** challenge: cert-manager proves
  control of your domain by writing a TXT record through your DNS provider's API. Let's Encrypt
  never connects to your network. The `ingress` chart
  ([`deploy/charts/ingress`](../../deploy/charts/ingress)) renders the `Issuer`, the `Certificate`
  and the Traefik `Ingress`.
- **Split DNS**: the three host names resolve, on the home network only, to the LAN address of the
  server. The public DNS zone holds no A record for them.
- **Port 443 only**: RKE2's bundled Traefik runs without its `web` entrypoint
  ([`deploy/rke2/rke2-traefik-config.yaml`](../../deploy/rke2/rke2-traefik-config.yaml)).

Cloudflare is the reference DNS provider: the chart's solver is `dns01.cloudflare`. Another
provider needs a change to the chart's `Issuer` template (and possibly to the keys of the Secret
contract); it is not a value.

## What you need

- A domain you own whose DNS zone is hosted at Cloudflare, e.g. `example.org`, and a dedicated
  subdomain of it as the chart's `domain`, e.g. `coldframe.example.org`. Prefer a dedicated
  subdomain over the apex: the LAN record for the web host would hide any public site at the apex.
  The hosts are, by default, `coldframe.example.org` (web app), `api.coldframe.example.org` (Server API, SignalR) and `auth.coldframe.example.org`
  (Keycloak). The chart values `hosts.web`, `hosts.api` and `hosts.auth` override them.
- The **LAN ingress address**: the server node's fixed address on the home network, e.g.
  `192.168.1.10`, where Traefik listens on 443 (hostPort). Reserve it in the router's DHCP.
- A resolver on the home network that you control and that can answer for single host names: the
  router's local DNS (host overrides) or a local DNS server (Pi-hole, AdGuard Home, Unbound,
  dnsmasq), handed out by DHCP to every device.
- cert-manager 1.21.2 installed ([`deploy/charts/README.md`](../../deploy/charts/README.md#prerequisites))
  and the rest of the stack installed or about to be.

## 1. The DNS-01 token

In the Cloudflare dashboard, create an API token (My Profile → API Tokens → Create Token) with:

- Permissions: **Zone → DNS → Edit** and **Zone → Zone → Read**;
- Zone resources: **Include → Specific zone → your zone** (not all zones).

Store it as the Secret `coldframe-dns01`, key `api-token`, in the namespace of the releases
([`deploy/SECRETS.md`](../../deploy/SECRETS.md)):

```sh
NS=coldframe
kubectl -n "$NS" create secret generic coldframe-dns01 --from-literal=api-token='<cloudflare-api-token>'
```

The token can change DNS records of the zone: keep it out of Git and shell history, and rotate it
at Cloudflare if it leaks. To rotate: create a new token, update the Secret, force a renewal
(`cmctl renew coldframe-tls -n "$NS"`, or delete the Secret `coldframe-tls`), wait for the
Certificate to be Ready (`kubectl -n "$NS" wait certificate/coldframe-tls --for=condition=Ready`),
and only then revoke the old token. cert-manager reads the Secret at every challenge.

## 2. cert-manager checks the challenge on public resolvers

Before it asks Let's Encrypt to validate, cert-manager checks that the `_acme-challenge` TXT
record is visible. By default it asks the cluster's resolvers, and with split DNS those may answer
for your domain locally (or forward to a local server that knows only the three host overrides),
so the check never sees the TXT record and the certificate stays pending. Make cert-manager ask
public recursive resolvers only.

**With Fleet** ([`install.md`](install.md)), the `cert-manager` bundle
([`deploy/fleet/cert-manager/fleet.yaml`](../../deploy/fleet/cert-manager/fleet.yaml)) starts
cert-manager with both flags (chart `extraArgs`), also after every cert-manager upgrade: skip the
patch below and only check the args with the last command of the block. The patch is for a
cert-manager installed by hand from the manifest:

```sh
# Adds each flag only when it is missing: safe to run again, e.g. after a cert-manager upgrade.
for flag in --dns01-recursive-nameservers-only --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53; do
  kubectl -n cert-manager get deployment cert-manager \
    -o jsonpath='{.spec.template.spec.containers[0].args}' | grep -F -- "\"${flag}\"" >/dev/null \
    || kubectl -n cert-manager patch deployment cert-manager --type=json \
      -p "[{\"op\": \"add\", \"path\": \"/spec/template/spec/containers/0/args/-\", \"value\": \"${flag}\"}]"
done
kubectl -n cert-manager rollout status deployment/cert-manager
kubectl -n cert-manager get deployment cert-manager -o jsonpath='{.spec.template.spec.containers[0].args}{"\n"}'
```

The last command must list both flags once. With a manual install, after every cert-manager
upgrade, check the args and patch only if the flags are missing (the loop above does both). The
node must be allowed to reach 1.1.1.1 and 9.9.9.9 on port 53 (outbound only).

## 3. Traefik on 443 only

Apply [`deploy/rke2/rke2-traefik-config.yaml`](../../deploy/rke2/rke2-traefik-config.yaml) (with
Fleet, the `rke2-traefik` bundle applies it) and verify that the `rke2-traefik` Service lists 443
only, as [`deploy/rke2/README.md`](../../deploy/rke2/README.md) describes. Do not forward any port
from the internet to the server: no inbound traffic is needed, not even for renewals.

## 4. Install the ingress chart

With Fleet, the `ingress` bundle installs it with the `domain` and `acme.email` of the `ingress`
key of the ConfigMap `coldframe-values` ([`install.md`](install.md#3-the-site-values)); by hand:

```sh
NS=coldframe
helm install ingress deploy/charts/ingress -n "$NS" --wait \
  --set domain=coldframe.example.org \
  --set acme.email=you@example.org
```

`acme.email` is optional: only a contact address for the ACME account (Let's Encrypt no longer
sends expiry emails). Renewal failures show up only through the certificate check of step 6. To try the setup without touching the
production rate limits, add `--set acme.server=https://acme-staging-v02.api.letsencrypt.org/directory`
first: staging certificates are not publicly trusted, so the checks in step 7 fail on them until
you switch back to production (`helm upgrade` with the default server, then
`kubectl -n "$NS" delete secret coldframe-tls` to issue again).

The other charts must use the public host names: the `keycloak` chart `hostname=auth.coldframe.example.org`,
the `server` chart `identity.authority=https://auth.coldframe.example.org/realms/coldframe`, and the `web`
chart `keycloak.issuer=https://auth.coldframe.example.org/realms/coldframe` and
`origin=https://coldframe.example.org` ([`deploy/charts/README.md`](../../deploy/charts/README.md#releases)).
The realm's redirect URIs must name `https://coldframe.example.org`.

Watch the certificate being issued (usually within two minutes):

```sh
kubectl -n "$NS" get issuer coldframe-letsencrypt        # READY True: the ACME account exists
kubectl -n "$NS" get certificate coldframe-tls           # READY True
kubectl -n "$NS" get certificaterequests,orders,challenges
kubectl -n "$NS" describe challenge                      # while pending: why
```

A challenge stuck on "propagation check failed" means step 2 is missing or the node cannot reach
the public resolvers. "Cloudflare API error" means the token lacks a permission or the zone.

## 5. Split DNS records

On the router's local DNS or the local DNS server, add **one record per host**, each pointing to
the LAN ingress address:

| Host | Type | Value |
| --- | --- | --- |
| `coldframe.example.org` | A | `192.168.1.10` |
| `api.coldframe.example.org` | A | `192.168.1.10` |
| `auth.coldframe.example.org` | A | `192.168.1.10` |

Add host records, **not a local zone for the whole domain**: a local zone would answer (or deny)
every other name of the domain on the LAN, including `_acme-challenge` TXT records and any public
service you run under it. No public A record is needed; leave the public zone without one for
these hosts, so nothing outside the home network is pointed at it. In dnsmasq or Pi-hole, use the
per-host form (dnsmasq `host-record=coldframe.example.org,192.168.1.10`, or Pi-hole's Local DNS
records / hosts entries), never `address=/coldframe.example.org/192.168.1.10`: that form matches
every subdomain too, `_acme-challenge` included.

### The node and the cluster must use this resolver

The Server and the web app validate tokens against `https://auth.coldframe.example.org/realms/coldframe`
from inside the cluster, so the node, and CoreDNS through it, must resolve `auth.coldframe.example.org` to
the LAN address:

```sh
resolvectl status                    # on the node: the DNS server is the LAN resolver
getent hosts auth.coldframe.example.org        # on the node: 192.168.1.10
kubectl run dnscheck --rm -it --restart=Never --image=docker.io/library/busybox:1.37 -- \
  nslookup auth.coldframe.example.org          # in the cluster: 192.168.1.10
```

If the node uses a public resolver (a static `/etc/resolv.conf`, systemd-resolved with a fallback
server), point it to the LAN resolver. CoreDNS forwards to the node's resolvers
(`/etc/resolv.conf`); after changing them, restart CoreDNS
(`kubectl -n kube-system rollout restart deployment/rke2-coredns-rke2-coredns`).

### Phones must not bypass the LAN resolver

A phone that sends DNS elsewhere gets no answer for the hosts (there is no public record):

- **Android**: Settings → Network & internet → Private DNS: **Off** or **Automatic**, not a
  hostname such as `dns.google`.
- **iOS**: no DNS profile or VPN app that overrides DNS (Settings → General → VPN & Device
  Management).
- **Browsers** (desktop and mobile): turn off secure DNS / DNS over HTTPS, or set it to use the
  system resolver (Chrome: Settings → Privacy and security → Security → Use secure DNS; Firefox:
  Settings → Privacy & Security → DNS over HTTPS: Off or default protection).
- The router must hand out only the LAN resolver by DHCP (no public secondary DNS server).

Outside the home network the hosts do not resolve: Coldframe is local-first and reachable only on
the LAN (or through a VPN into it).

## 6. Renewals

cert-manager renews the certificate at two thirds of its lifetime (its default), through the same
DNS-01 challenge, and Traefik picks up the renewed Secret without a restart. Check it now and then:

```sh
kubectl -n "$NS" get certificate coldframe-tls \
  -o jsonpath='ready={.status.conditions[?(@.type=="Ready")].status} notAfter={.status.notAfter} renewalTime={.status.renewalTime}{"\n"}'
```

`ready` must be `True` and `renewalTime` before `notAfter`. A `renewalTime` in the past with the
old `notAfter` means renewals fail: see step 4's diagnostics. A renewal can be forced with
`cmctl renew coldframe-tls -n "$NS"`, or by deleting the Secret `coldframe-tls`.

## 7. Verify

From a laptop on the home network:

```sh
D=coldframe.example.org
for host in "$D" "api.$D" "auth.$D"; do
  dig +short "$host"                                   # 192.168.1.10
done
openssl s_client -connect "auth.$D:443" -servername "auth.$D" -verify_return_error </dev/null 2>/dev/null \
  | grep -E 'subject=|issuer=|Verify return code'      # issuer Let's Encrypt, "0 (ok)"
openssl s_client -connect "auth.$D:443" -servername "auth.$D" </dev/null 2>/dev/null \
  | openssl x509 -noout -ext subjectAltName -enddate   # the three hosts, the expiry
curl -fsS "https://api.$D/.well-known/healthz"                                        # Healthy
curl -fsS "https://auth.$D/realms/coldframe/.well-known/openid-configuration" | jq -r .issuer
                                                       # https://auth.coldframe.example.org/realms/coldframe
curl -fsS -o /dev/null -w '%{http_code}\n' "https://$D/.well-known/healthz/ready"     # 200
curl --max-time 5 "http://$D/"                        # must fail: nothing serves port 80
```

`curl` uses the system's public roots: no `--cacert` and no `-k`.

Nothing serves port 80, by design, so a host typed without a scheme fails with "connection
refused": open the `https://` URL (and bookmark it).

**From a phone on the home Wi-Fi** (the operator's acceptance check):

1. Wi-Fi on, mobile data off, Private DNS as in step 5.
2. Open `https://coldframe.example.org` in the browser: the page loads with the padlock and **no
   certificate warning**. The certificate details show the host name, issued by Let's Encrypt.
3. Open `https://auth.coldframe.example.org/realms/coldframe/.well-known/openid-configuration`: JSON whose
   `issuer` is `https://auth.coldframe.example.org/realms/coldframe`.
4. Sign in to the Coldframe app: it reaches the Server and Keycloak without a warning.

If the phone says the site cannot be reached, it is not using the LAN resolver (step 5). If it
warns about the certificate, the certificate is from staging or the private-CA fallback.

## Fallback: a private CA (unsupported)

Coldframe supports publicly trusted certificates only: the Hub and the apps trust public roots,
and nothing installs a private root on them. Where DNS-01 with a public domain is impossible, the
chart can use an Issuer you manage instead, at your own risk: create it in the namespace (for
example a cert-manager CA Issuer), then

```sh
helm install ingress deploy/charts/ingress -n "$NS" --wait \
  --set domain=home.arpa --set acme.enabled=false --set externalIssuer.name=my-ca
```

No `Issuer` is rendered and the `Certificate` references `my-ca` (`externalIssuer.kind` selects
`ClusterIssuer`). Every device must then trust your CA's root, which the Hub cannot. The chart
refuses to render with `acme.enabled=false` and no `externalIssuer.name`. The CI smoke install
takes this path with a throwaway CA, because a disposable cluster cannot complete DNS-01.
