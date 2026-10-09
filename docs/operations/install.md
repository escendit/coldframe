# Install Coldframe with Fleet (GitOps)

The reference deployment is a single-node RKE2 server on which Fleet installs the whole stack from
this repository at a release tag (Story 2.5a, AD-22). One GitRepo,
[`deploy/fleet/gitrepo.yaml`](../../deploy/fleet/gitrepo.yaml), points Fleet at eleven bundle
folders; each bundle is one Helm release, and the bundles wait for each other through `dependsOn`.
An upgrade is a change of the GitRepo's `revision` to a newer tag. `revision` must name an
existing release tag that contains `deploy/fleet`, that is the first release cut after Story 2.5a
or a later one; no earlier tag has the bundles, and the tag in the file is a placeholder to change
(step 4). Nothing secret is in Git: the Secrets, the realm and the site values are created once,
out of band.

| Bundle | Folder | Release (namespace) | Waits for |
| --- | --- | --- | --- |
| `cert-manager` | [`deploy/fleet/cert-manager`](../../deploy/fleet/cert-manager/fleet.yaml) | `cert-manager` (`cert-manager`) | nothing |
| `cloudnative-pg` | [`deploy/fleet/cloudnative-pg`](../../deploy/fleet/cloudnative-pg/fleet.yaml) | `cnpg` (`cnpg-system`) | nothing |
| `barman-cloud` | [`deploy/fleet/barman-cloud`](../../deploy/fleet/barman-cloud/fleet.yaml) | `barman-cloud` (`cnpg-system`) | cert-manager, cloudnative-pg |
| `rke2-traefik` | [`deploy/rke2`](../../deploy/rke2/fleet.yaml) | `rke2-traefik-config` (`kube-system`) | nothing |
| `database` | [`deploy/charts/database`](../../deploy/charts/database/fleet.yaml) | `database` (`coldframe`) | cloudnative-pg, barman-cloud |
| `ingress` | [`deploy/charts/ingress`](../../deploy/charts/ingress/fleet.yaml) | `ingress` (`coldframe`) | cert-manager, rke2-traefik, database |
| `nats`, `temporal`, `keycloak` | `deploy/charts/<name>` | `<name>` (`coldframe`) | database, ingress |
| `server` | [`deploy/charts/server`](../../deploy/charts/server/fleet.yaml) | `server` (`coldframe`) | nats, temporal, keycloak |
| `web` | [`deploy/charts/web`](../../deploy/charts/web/fleet.yaml) | `web` (`coldframe`) | server, keycloak |

A bundle is Ready when everything it deployed is: Deployments rolled out, the CloudNativePG
`Cluster` Ready, the `Certificate` Ready. The operator versions are pinned in
[`deploy/charts/dependencies.env`](../../deploy/charts/dependencies.env) (cert-manager v1.21.2,
CloudNativePG 1.30.1 through chart 0.29.1, the Barman Cloud plugin v0.15.0 through chart 0.8.0,
Fleet v0.16.2). The cert-manager bundle starts cert-manager with the DNS-01 resolver flags of
[`split-dns.md`](split-dns.md#2-cert-manager-checks-the-challenge-on-public-resolvers); no patch
is needed.

CI checks the bundles on every pull request ([`deploy/charts/test.sh`](../../deploy/charts/test.sh)):
it renders the GitRepo's paths with the fleet CLI, checks the labels, the `dependsOn` order, the
pins and the site values ([`deploy/fleet/check-fleet.py`](../../deploy/fleet/check-fleet.py)), and
renders every chart with its `fleet.yaml` values and the example `coldframe-values`. The proof on a
cluster (ordered install, upgrade and restart durability under Fleet) is the Fleet smoke,
[`deploy/fleet/smoke.sh`](../../deploy/fleet/smoke.sh), in the `Images` CI job. The checklist at
the end is what CI cannot do: your domain, your phone, your S3 bucket.

## Prerequisites

- A single-node **RKE2 v1.36.4+rke2r1** server with its bundled Traefik as the ingress controller
  ([`deploy/rke2/README.md`](../../deploy/rke2/README.md)), and `kubectl` and `helm` with
  cluster-admin access to it. Do not apply `rke2-traefik-config.yaml` by hand: the `rke2-traefik`
  bundle does.
- A domain with its DNS zone at Cloudflare, a dedicated subdomain for Coldframe (for example
  `coldframe.example.org`), and split DNS for its three hosts: steps 1 and 5 of
  [`split-dns.md`](split-dns.md).
- An S3 bucket off the node for the database backups, and its endpoint.
- Outbound access from the node to `github.com` (the GitRepo and the Fleet charts), the operators'
  Helm repositories (`charts.jetstack.io`, `cloudnative-pg.github.io`), the container registries
  (`ghcr.io`, `docker.io`, `quay.io`), Let's Encrypt and Cloudflare, and the public resolvers
  1.1.1.1 and 9.9.9.9 on port 53.
- On the machine you run the commands from: `curl` and `sha256sum` (step 1), and `openssl`, `jq`
  and the `aws` CLI for the verification checklist.

## 1. Install Fleet

Fleet v0.16.2 on the cluster itself: it registers the cluster as `local` in the namespace
`fleet-local`, where the GitRepo goes. The two charts are the release assets pinned in
[`deploy/charts/dependencies.env`](../../deploy/charts/dependencies.env) (`FLEET_CRD_CHART_URL`,
`FLEET_CHART_URL`), checked against their sha256 before Helm installs them:

```sh
source deploy/charts/dependencies.env
curl -fsSLo fleet-crd.tgz "$FLEET_CRD_CHART_URL"
curl -fsSLo fleet.tgz "$FLEET_CHART_URL"
printf '%s  fleet-crd.tgz\n%s  fleet.tgz\n' "$FLEET_CRD_CHART_SHA256" "$FLEET_CHART_SHA256" | sha256sum --check --strict
helm install fleet-crd ./fleet-crd.tgz -n cattle-fleet-system --create-namespace --wait
helm install fleet ./fleet.tgz -n cattle-fleet-system --wait
kubectl -n fleet-local get clusters.fleet.cattle.io    # "local", with a LAST-SEEN time
```

## 2. Secrets and the realm

In the namespace `coldframe`, create every Secret of [`deploy/SECRETS.md`](../../deploy/SECRETS.md)
(the database Secrets with the label `cnpg.io/reload=true`, `coldframe-backup-s3`, `coldframe-dns01`,
the Device enrolment key `coldframe-enrolment-key` and the Device key-encryption key
`coldframe-device-kek`, and the others; record the `coldframe-device-kek` value first and keep it
apart from the database backups and their credentials, since no enrolled Device works without it and
together with a backup it decrypts every Device key), and the ConfigMap `coldframe-realm` holding the `coldframe` realm file with the
redirect URIs of your hosts ([`deploy/charts/README.md`](../../deploy/charts/README.md#prerequisites),
items 1 to 4):

```sh
kubectl create namespace coldframe
# kubectl -n coldframe create secret generic ...   (deploy/SECRETS.md)
kubectl -n coldframe create configmap coldframe-realm --from-file=coldframe-realm.json=<your-realm.json>
```

`coldframe-push` is the one Secret you may leave out: without it the Server runs and sends no push
notification. To notify phones, create it and set `push.apns.enabled: true` and/or
`push.fcm.enabled: true` (and `push.apns.topic`, your iOS bundle ID) under the `server` key of your
site values ([`deploy/SECRETS.md`](../../deploy/SECRETS.md#push-notifications),
[`docs/bench/push-checklist.md`](../bench/push-checklist.md)).

A missing Secret leaves its pod in `CreateContainerConfigError` and its bundle not Ready, which
holds back every bundle after it.

## 3. The site values

Copy [`deploy/fleet/values.example.yaml`](../../deploy/fleet/values.example.yaml), replace every
`<placeholder>` (domain, S3 endpoint and bucket, ACME email), and apply it:

```sh
cp deploy/fleet/values.example.yaml coldframe-values.yaml
$EDITOR coldframe-values.yaml
kubectl apply -f coldframe-values.yaml
```

It is the ConfigMap `coldframe-values` in `coldframe`, one key per release (`database`, `keycloak`,
`server`, `web`, `ingress`), each holding that release's Helm values. Keep your copy somewhere
safe (it holds no secret). Never set `image.tag`: the chart's `appVersion` is the release tag.

## 4. Apply the GitRepo

```sh
kubectl apply -f deploy/fleet/gitrepo.yaml
```

First set `spec.revision` in the file to the release you install: an existing release tag
`vX.Y.Z` that contains `deploy/fleet` (the first release cut after Story 2.5a, or a later one).
The tag in the file is only an example and may not exist yet. The charts and the images of that
tag are installed together; `deploy/charts/test.sh` fails when `revision` is not a tag of that
form.

## 5. Watch the bundles

```sh
kubectl -n fleet-local get gitrepo coldframe            # the commit it read, BUNDLEDEPLOYMENTS-READY 11/11
kubectl -n fleet-local get bundles                      # one per folder, READY 1/1 each
kubectl get bundledeployments -A                        # per bundle: state and message
kubectl -n coldframe get pods,jobs
```

The operators and `rke2-traefik` come first, then `database` (the cluster takes a few minutes),
`ingress` (until the certificate is issued, usually within two minutes), `nats`, `temporal` and
`keycloak`, then `server` (its migration Job runs first) and `web`. A bundle that waits shows
`NotReady` or `WaitApplied` with a message naming the bundle it depends on; the first bundle in
the chain that is not Ready is the one to look at:

```sh
kubectl -n fleet-local get bundles -o custom-columns=NAME:.metadata.name,READY:.status.display.readyClusters,STATE:.status.display.state
kubectl -n fleet-local describe bundle <name>           # Conditions: the message of the agent
```

Common causes: a missing Secret or key (the pod names it), a wrong value in `coldframe-values`
(the bundle deployment's message shows the Helm error), an S3 endpoint the node cannot reach (the
`Cluster` is not Ready, `ContinuousArchiving` is False), a DNS-01 token without the right
permissions or a DNS-01 check that never sees the TXT record (the `Certificate` stays pending:
[`split-dns.md`](split-dns.md#4-install-the-ingress-chart)). A `Certificate` stuck on DNS-01 keeps
the `ingress` bundle not Ready, and with it `nats`, `temporal`, `keycloak`, `server` and `web`,
which all wait for `ingress`:

```sh
kubectl -n coldframe describe certificate coldframe-tls
kubectl -n coldframe get challenges                     # the state and reason of each DNS-01 challenge
```

## Upgrades

An upgrade is a new `revision`:

```sh
kubectl -n fleet-local patch gitrepo coldframe --type=merge -p '{"spec":{"revision":"v0.2.0"}}'
```

Fleet reads the tag, and every bundle whose chart changed is upgraded with Helm. For the Server,
the migration Job of the new version runs first and must succeed; only then does the Server
Deployment change, stopping the old pod before the new one starts (strategy `Recreate`,
[`deploy/charts/README.md`](../../deploy/charts/README.md#upgrades)). When the migration fails, the
bundle shows the failed upgrade and the running Server stays on the old version. Migrations are
forward-only: going back to an older tag after a migration ran is not supported; restore instead
([`restore.md`](restore.md)).

Read the release notes before an upgrade that changes an operator pin: CloudNativePG and
cert-manager upgrades roll their operators, and the database may restart once.

## Changing the site values

Edit your copy of `coldframe-values` and apply it again. Fleet reads the ConfigMap when it deploys
a bundle; to deploy the change now, force a redeploy of the GitRepo's bundles:

```sh
kubectl apply -f coldframe-values.yaml
kubectl -n fleet-local patch gitrepo coldframe --type=merge \
  -p "{\"spec\":{\"forceSyncGeneration\":$(date +%s)}}"
```

## Moving from a manual install

An installation made by hand ([`deploy/charts/README.md`](../../deploy/charts/README.md#install))
moves to Fleet in place, without losing data:

1. Write `coldframe-values` (step 3) with the values you passed to `helm install` with `--set`:
   from now on, Fleet's upgrades use only the values in `fleet.yaml` and in this ConfigMap.
2. If you placed `rke2-traefik-config.yaml` in RKE2's manifests directory
   ([`deploy/rke2/README.md`](../../deploy/rke2/README.md#apply)), remove it there, on the server
   node as root. RKE2 re-applies that directory at every start and would fight Fleet over the
   object; the `HelmChartConfig` itself stays, and the `rke2-traefik` bundle adopts it:

   ```sh
   rm -f /var/lib/rancher/rke2/server/manifests/rke2-traefik-config.yaml
   ```

3. Delete the two operator Deployments of the kubectl-installed manifests. The pinned charts
   select their pods with other labels than the manifests (CloudNativePG chart 0.29.1:
   `app.kubernetes.io/name: cloudnative-pg, app.kubernetes.io/instance: cnpg` against
   `app.kubernetes.io/name: cloudnative-pg`; plugin chart 0.8.0:
   `app.kubernetes.io/name: plugin-barman-cloud, app.kubernetes.io/instance: barman-cloud` against
   `app: barman-cloud`), and a Deployment's `spec.selector` cannot change, so adopting them would
   fail the Helm upgrade and hold back every bundle after it. Only these two go: the CRDs, the
   `Cluster`, its volumes and its data stay, and the database keeps running. The operator and the
   plugin are down only until their bundles install them again (backups and failover wait until
   then):

   ```sh
   kubectl -n cnpg-system delete deployment cnpg-controller-manager barman-cloud
   ```

4. Install Fleet (step 1) and apply the GitRepo (step 4) at the tag you run.
5. The Coldframe releases keep their names and namespace (`database`, `nats`, `temporal`,
   `keycloak`, `server`, `web`, `ingress` in `coldframe`): Fleet upgrades them in place. The
   operator bundles adopt the other objects of the kubectl-installed manifests (`takeOwnership`;
   step 3 removed the two Deployments they cannot adopt), and so does the `rke2-traefik` bundle
   with a `HelmChartConfig` applied with kubectl. The `cert-manager` bundle sets the DNS-01
   resolver flags itself: the manual patch is no longer needed.
6. Once every bundle is Ready, delete what the operator manifests created and the charts do not
   (they are no longer used); list them first:

   ```sh
   kubectl get clusterroles,clusterrolebindings -o name | grep -E '^[^/]+/cnpg-(manager|database-|publication-|subscription-)'
   kubectl get clusterroles,clusterrolebindings -o name | grep -E 'barman|plugin'
   kubectl -n cnpg-system get serviceaccounts,configmaps,issuers,roles,rolebindings -o name
   ```

   The CloudNativePG chart's names are `cnpg-controller-manager*`; the manifest's service account
   `cnpg-manager` and its cluster roles `cnpg-manager`, `cnpg-*-editor-role` and
   `cnpg-*-viewer-role` are the leftovers. From the Barman Cloud plugin manifest, the service
   account `plugin-barman-cloud`, the Issuer `selfsigned-issuer` and the roles and bindings that
   grant that service account its rights are the leftovers; keep what the `barman-cloud` release
   owns (`kubectl get <kind> <name> -o jsonpath='{.metadata.annotations.meta\.helm\.sh/release-name}'`
   prints `barman-cloud`).

Do not delete the GitRepo or remove a path from it while the stack runs: Fleet uninstalls the
bundles it no longer has, and **uninstalling the `database` release deletes the cluster and its
volumes**; only the backups remain ([`restore.md`](restore.md)).

## Verification checklist (on the home server)

Tick each item after an install, and again after every upgrade:

- [ ] **Bundles and pods ready**: `kubectl -n fleet-local get bundles` shows every bundle `1/1`,
  and `kubectl -n coldframe get pods` shows every pod `Running` and Ready, or `Completed` for the
  Jobs (`server-migrations`, `temporal-schema`, `temporal-namespace`).
- [ ] **Valid HTTPS on the three hosts**, from a laptop on the LAN (`D` is your domain):

  ```sh
  D=coldframe.example.org
  for host in "$D" "api.$D" "auth.$D"; do
    openssl s_client -connect "$host:443" -servername "$host" -verify_return_error </dev/null 2>/dev/null \
      | grep -E 'issuer=|Verify return code'                 # Let's Encrypt, "0 (ok)"
  done
  curl -fsS "https://api.$D/.well-known/healthz"             # Healthy
  curl -fsS "https://auth.$D/realms/coldframe/.well-known/openid-configuration" | jq -r .issuer
  curl -fsS -o /dev/null -w '%{http_code}\n' "https://$D/"    # 200 or a redirect to sign-in
  curl --max-time 5 "http://$D/"                             # fails: nothing on port 80
  ```

- [ ] **DNS-01 flags and Traefik on 443 only**: cert-manager runs with both DNS-01 flags once, and
  the `rke2-traefik` Service lists 443 only:

  ```sh
  kubectl -n cert-manager get deployment cert-manager -o jsonpath='{.spec.template.spec.containers[0].args}{"\n"}'
                                              # --dns01-recursive-nameservers-only and
                                              # --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53, once each
  kubectl -n kube-system get svc rke2-traefik # PORT(S): 443/TCP only, no 80
  ```

- [ ] **Sign-in from a phone and a browser**: on the home Wi-Fi, open `https://<domain>` on a phone
  (no certificate warning, [`split-dns.md`](split-dns.md#phones-must-not-bypass-the-lan-resolver))
  and in a desktop browser, and sign in; your Sites and Lots are listed.
- [ ] **Restart durability**: note a Site and a Lot, then, waiting after each step until every
  bundle and pod is Ready again and the Site and the Lot are still there:
  1. `kubectl -n coldframe delete pod -l app.kubernetes.io/instance=server,app.kubernetes.io/component!=migrations`
  2. `kubectl -n coldframe delete pod -l cnpg.io/cluster=coldframe-db,cnpg.io/instanceRole=primary`
  3. reboot the node (`sudo systemctl reboot`).
- [ ] **A backup in S3**: a Backup of the ScheduledBackup is `completed`, and the bucket holds it:

  ```sh
  kubectl -n coldframe get backups --sort-by=.metadata.creationTimestamp
  kubectl -n coldframe get clusters.postgresql.cnpg.io coldframe-db \
    -o jsonpath='{.status.conditions[?(@.type=="ContinuousArchiving")].status}{"\n"}'   # True
  aws s3 ls --endpoint-url https://<s3-endpoint> s3://<bucket>/coldframe/coldframe-db/ --recursive | tail
  ```

  The listing shows `base/` (a base backup) and `wals/` objects dated today.
