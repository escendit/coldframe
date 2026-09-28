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
| `coldframe-db-server` | `kubernetes.io/basic-auth` | `username`, `password` | `server` chart (Server and migration Job); database `coldframe` |
| `coldframe-db-temporal` | `kubernetes.io/basic-auth` | `username`, `password` | `temporal` chart (server and schema Job); databases `temporal` and `temporal_visibility` |
| `coldframe-db-keycloak` | `kubernetes.io/basic-auth` | `username`, `password` | `keycloak` chart; database `keycloak` |
| `coldframe-keycloak-admin` | `Opaque` | `username`, `password` | `keycloak` chart: the temporary bootstrap admin of the master realm. Keycloak creates it on the first start only, but the Deployment reads the Secret on every start, so it must stay |
| `coldframe-oidc-clients` | `Opaque` | `web-client-secret`, `server-client-secret` | `web` chart (`web-client-secret`), `server` chart (`server-client-secret`), `keycloak` chart (both, substituted into an imported realm) |
| `coldframe-smtp` | `Opaque` | `host`, `port`, `username`, `password`, `from` | not yet consumed: invitations (Epic 9) |
| `coldframe-push` | `Opaque` | `apns-key.p8`, `apns-key-id`, `apns-team-id`, `fcm-service-account.json` | not yet consumed: push notifications (Epic 6) |
| `coldframe-dns01` | `Opaque` | `api-token` | not yet consumed: the cert-manager DNS-01 solver (Story 2.4) |
| `coldframe-enrolment-key` | `Opaque` | `private-key.pem` | not yet consumed: Device enrolment (Epic 3) |
| `coldframe-backup-s3` | `Opaque` | `access-key-id`, `secret-access-key` | not yet consumed: CloudNativePG backups (Story 2.3) |

The three database Secrets have the `kubernetes.io/basic-auth` shape that CloudNativePG consumes
for managed roles (Story 2.3). The Server composes its connection string from `username` and
`password`, so the Server's password must not contain `;`.

## Creating the Secrets

Replace every `<...>` with a real value, and keep the values out of shell history (for example,
read them from a password manager into variables). `NS` is the namespace of the releases.

```sh
NS=coldframe

kubectl -n "$NS" create secret generic coldframe-db-server \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=coldframe --from-literal=password='<server-db-password>'

kubectl -n "$NS" create secret generic coldframe-db-temporal \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=temporal --from-literal=password='<temporal-db-password>'

kubectl -n "$NS" create secret generic coldframe-db-keycloak \
  --type=kubernetes.io/basic-auth \
  --from-literal=username=keycloak --from-literal=password='<keycloak-db-password>'

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
  --from-literal=api-token='<dns-provider-api-token>'

kubectl -n "$NS" create secret generic coldframe-enrolment-key \
  --from-file=private-key.pem='<path/to/enrolment-private-key.pem>'

kubectl -n "$NS" create secret generic coldframe-backup-s3 \
  --from-literal=access-key-id='<s3-access-key-id>' --from-literal=secret-access-key='<s3-secret-access-key>'
```

The database roles and the databases themselves belong to the database cluster (Story 2.3); the
passwords above must be the passwords of those roles. The client secrets must match the
`coldframe-web` and `coldframe-server` clients of the `coldframe` realm.

After the first start, sign in with the bootstrap admin, create a permanent admin in the master
realm and delete the temporary one. Keep the `coldframe-keycloak-admin` Secret: without it the
Keycloak pod does not start (`CreateContainerConfigError`).

Rotating a value: change it at its source first, then update the Secret, then restart the pods
that read it (`kubectl -n "$NS" rollout restart deployment/<release>`). Pods read Secrets only at
start.

- `coldframe-db-*`: change the role's password in PostgreSQL first (Story 2.3); the Secret alone
  does not change it.
- `coldframe-oidc-clients`: regenerate the client secret in Keycloak first (admin console or admin
  API, clients `coldframe-web` and `coldframe-server`). The realm import never overwrites an
  existing realm, so a new value in the Secret alone breaks sign-in.
