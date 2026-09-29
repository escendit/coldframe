# Secret contract

Every credential Coldframe needs is a Kubernetes Secret that the adopter creates out of band, in
the namespace of the releases, before the first install. **The charts never create, template or
generate a Secret**, and no secret value is ever committed to Git (AD-15). The names and keys
below are fixed in the chart templates; they are not values, so this list is the whole contract.

`deploy/charts/test.sh` fails when a chart renders a Secret, or references a Secret name or key
that is not in the table below.

A missing Secret or key does not fall back to anything: the pod stays in
`CreateContainerConfigError`, and `kubectl describe pod` names the Secret or key.

| Secret | Type | Keys | Consumer |
| --- | --- | --- | --- |
| `coldframe-db-coldframe` | `kubernetes.io/basic-auth` | `username`, `password` | `database` chart: password of the role `coldframe`, owner of the database `coldframe`; `server` chart (Server and migration Job) |
| `coldframe-db-temporal` | `kubernetes.io/basic-auth` | `username`, `password` | `database` chart: password of the role `temporal`, owner of the databases `temporal` and `temporal_visibility`; `temporal` chart (server and schema Job) |
| `coldframe-db-keycloak` | `kubernetes.io/basic-auth` | `username`, `password` | `database` chart: password of the role `keycloak`, owner of the database `keycloak`; `keycloak` chart |
| `coldframe-keycloak-admin` | `Opaque` | `username`, `password` | `keycloak` chart: the temporary bootstrap admin of the master realm. Keycloak creates it on the first start only, but the Deployment reads the Secret on every start, so it must stay |
| `coldframe-oidc-clients` | `Opaque` | `web-client-secret`, `server-client-secret` | `web` chart (`web-client-secret`), `server` chart (`server-client-secret`), `keycloak` chart (both, substituted into an imported realm) |
| `coldframe-smtp` | `Opaque` | `host`, `port`, `username`, `password`, `from` | not yet consumed: invitations (Epic 9) |
| `coldframe-push` | `Opaque` | `apns-key.p8`, `apns-key-id`, `apns-team-id`, `fcm-service-account.json` | not yet consumed: push notifications (Epic 6) |
| `coldframe-dns01` | `Opaque` | `api-token` | `ingress` chart: the Cloudflare API token of the cert-manager DNS-01 solver of the Issuer `coldframe-letsencrypt`; permissions Zone → DNS → Edit and Zone → Zone → Read, on the domain's zone only |
| `coldframe-enrolment-key` | `Opaque` | `private-key.pem` | `server` chart: the X25519 enrolment private key, PKCS#8 PEM. Devices seal their key to its public key (Device enrolment) |
| `coldframe-device-kek` | `Opaque` | `kek` | `server` chart: the key-encryption key of every enrolled Device's key, at least 32 characters |
| `coldframe-backup-s3` | `Opaque` | `access-key-id`, `secret-access-key` | `database` chart: the Barman Cloud `ObjectStore` (WAL archive and base backups, and the source of a restore) |

The three database Secrets have the `kubernetes.io/basic-auth` shape that CloudNativePG consumes
for managed roles: the `database` chart creates each role with the password of its Secret and
keeps it equal to that Secret. Each is named after its role and database (`coldframe-db-<role>`):
CloudNativePG itself creates `coldframe-db-ca`, `coldframe-db-server`, `coldframe-db-replication`,
`coldframe-db-app` and `coldframe-db-superuser` for the cluster `coldframe-db`, so no contract
Secret may take those names. **Each Secret's `username` must equal its role name**, the chart
values `roles.server`, `roles.temporal` and `roles.keycloak` (defaults `coldframe`, `temporal`,
`keycloak`); the consumers log in with `username`. Label them `cnpg.io/reload=true` so that
CloudNativePG applies a changed password at once. The Server composes its connection string from
`username` and `password`, so the Server's password must not contain `;`.

cert-manager generates two Secrets for the `ingress` chart: `coldframe-tls` (the certificate and
key the Ingress serves) and `coldframe-letsencrypt-account` (the ACME account key of the Issuer).
They are not part of the contract, and no contract Secret may take those names. The DNS-01 token
is read from the releases' namespace, which is why the chart uses a namespaced `Issuer` and not a
`ClusterIssuer` ([`docs/operations/split-dns.md`](../docs/operations/split-dns.md)).

`coldframe-backup-s3` holds the S3 access key of the backup bucket (`backup.destinationPath` and
`backup.endpointURL` of the `database` chart). The key needs read, write, list and delete on that
bucket: the plugin deletes backups past the retention policy.

## Creating the Secrets

Replace every `<...>` with a real value, and keep the values out of shell history (for example,
read them from a password manager into variables). `NS` is the namespace of the releases.

```sh
NS=coldframe

kubectl -n "$NS" create secret generic coldframe-db-coldframe \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=coldframe --from-literal=password='<server-db-password>'

kubectl -n "$NS" create secret generic coldframe-db-temporal \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=temporal --from-literal=password='<temporal-db-password>'

kubectl -n "$NS" create secret generic coldframe-db-keycloak \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=keycloak --from-literal=password='<keycloak-db-password>'

kubectl -n "$NS" label secret coldframe-db-coldframe coldframe-db-temporal coldframe-db-keycloak \
  cnpg.io/reload=true

kubectl -n "$NS" create secret generic coldframe-keycloak-admin \
  --from-literal=username=admin --from-literal=password='<keycloak-admin-password>'

kubectl -n "$NS" create secret generic coldframe-oidc-clients \
  --from-literal=web-client-secret='<coldframe-web-client-secret>' \
  --from-literal=server-client-secret='<coldframe-server-client-secret>'

kubectl -n "$NS" create secret generic coldframe-smtp \
  --from-literal=host='<smtp-host>' --from-literal=port='<smtp-port>' \
  --from-literal=username='<smtp-username>' --from-literal=password='<smtp-password>' \
  --from-literal=from='<sender-address>'

kubectl -n "$NS" create secret generic coldframe-push \
  --from-file=apns-key.p8='<path/to/AuthKey.p8>' \
  --from-literal=apns-key-id='<apns-key-id>' --from-literal=apns-team-id='<apns-team-id>' \
  --from-file=fcm-service-account.json='<path/to/service-account.json>'

kubectl -n "$NS" create secret generic coldframe-dns01 \
  --from-literal=api-token='<cloudflare-api-token>'

openssl genpkey -algorithm X25519 -out enrolment-private-key.pem
kubectl -n "$NS" create secret generic coldframe-enrolment-key \
  --from-file=private-key.pem=enrolment-private-key.pem
shred -u enrolment-private-key.pem

# Generate the KEK, record it (see below) before you create the Secret, then create it from that value.
openssl rand -base64 32
kubectl -n "$NS" create secret generic coldframe-device-kek \
  --from-literal=kek='<device-kek>'

kubectl -n "$NS" create secret generic coldframe-backup-s3 \
  --from-literal=access-key-id='<s3-access-key-id>' --from-literal=secret-access-key='<s3-secret-access-key>'
```

Record the `coldframe-device-kek` value in a password manager or another offline store that is
**separate from the database backups and their credentials** (`coldframe-backup-s3`): anyone holding
both a backup and the KEK can decrypt every enrolled Device's key. A database restore needs the same
value. To read it back from the cluster:
`kubectl -n "$NS" get secret coldframe-device-kek -o jsonpath='{.data.kek}' | base64 --decode`.
The enrolment key needs no copy: it can be replaced at any time (see below).

The `database` chart creates the roles with these passwords and the databases they own. The client secrets must match the
`coldframe-web` and `coldframe-server` clients of the `coldframe` realm.

After the first start, sign in with the bootstrap admin, create a permanent admin in the master
realm and delete the temporary one. Keep the `coldframe-keycloak-admin` Secret: without it the
Keycloak pod does not start (`CreateContainerConfigError`).

Rotating a value: change it at its source first, then update the Secret, then restart the pods
that read it (`kubectl -n "$NS" rollout restart deployment/<release>`). Pods read Secrets only at
start.

- `coldframe-db-*`: update the Secret; CloudNativePG (managed roles of the `database` chart) sets
  the role's password to the new value, at once when the Secret carries the label
  `cnpg.io/reload=true`, otherwise at its next reconciliation. Then restart the consumer
  (`server`, `temporal` or `keycloak`). Do not change the password in PostgreSQL by hand: the
  operator sets it back to the Secret.
- `coldframe-backup-s3`: create the new key at the S3 provider and update the Secret. Delete the
  old key only after a WAL segment and a Backup made after the change have reached the bucket
  (`ContinuousArchiving` `True` on `clusters.postgresql.cnpg.io/coldframe-db`, a new Backup
  `completed`; see [`docs/operations/restore.md`](../docs/operations/restore.md#recovery-point)).
- `coldframe-dns01`: create a new token at Cloudflare and update the Secret. Then force a renewal
  (`cmctl renew coldframe-tls -n "$NS"`, or delete the Secret `coldframe-tls`) and wait for the
  Certificate to be Ready (`kubectl -n "$NS" wait certificate/coldframe-tls --for=condition=Ready`)
  before you revoke the old token. cert-manager reads the Secret at every challenge; nothing needs a
  restart.
- `coldframe-enrolment-key`: create a new key (`openssl genpkey -algorithm X25519`), update the Secret
  and restart the `server`. Enrolled Devices are unaffected, because their keys are stored under
  `coldframe-device-kek`. Only enrolments in flight fail: an app that fetched the old public key
  (`GET /enrolment-key`) must start the Device setup again, and it shows the new fingerprint.
- `coldframe-device-kek`: **do not rotate.** Every enrolled Device's key is stored encrypted under it,
  and re-wrapping them under a new key is not built yet: a changed or lost value makes every enrolled
  Device unusable, and each must be enrolled again. Keep the recorded value apart from the database
  backups and their credentials; a database restore needs the same value.
- `coldframe-oidc-clients`: regenerate the client secret in Keycloak first (admin console or admin
  API, clients `coldframe-web` and `coldframe-server`). The realm import never overwrites an
  existing realm, so a new value in the Secret alone breaks sign-in.
