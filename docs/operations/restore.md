# Restore the database from the off-node backups

All durable state of Coldframe (Sites, Lots, Memberships, Readings, Alerts, events, Temporal
history and the Keycloak realm) lives in one CloudNativePG cluster, `coldframe-db`, installed by
the `database` chart ([`deploy/charts/database`](../../deploy/charts/database)). The cluster
ships its WAL continuously and takes a daily base backup to the S3 target of
`backup.destinationPath` through the Barman Cloud plugin. This runbook brings the cluster back
from that target after the database volume, the node or the whole installation is lost (Story
2.3, AD-15, AD-17).

The CI smoke install ([`deploy/charts/smoke.sh`](../../deploy/charts/smoke.sh)) tests the core of
this runbook on every pull request: with the apps stopped, it writes a marker before a base
backup and one after it, uninstalls the release, waits as in step 3, installs in recovery mode
into a new folder as in step 4, and checks both markers, the row count of every table, WAL
archiving and the Server's health. It does not run the optional steps 2 and 5, nor restart NATS.

The commands name the CloudNativePG cluster as `clusters.postgresql.cnpg.io`: with Fleet
installed, a bare `cluster` means Fleet's `clusters.fleet.cattle.io`.

## Recovery point

A restore brings back every transaction whose WAL segment reached the object store. **Data
written after the last archived WAL segment is lost.** PostgreSQL archives a segment when it is
full (16 MB) or when `archive_timeout` expires. The chart sets `archive_timeout` explicitly from
`backup.archiveTimeout` (default `5min`), so the loss is at most about **five minutes of writes**
before the failure, plus whatever the S3 target had not yet acknowledged.

What is lost with those writes: accounts, sign-ins and sessions created in Keycloak after the
recovery point, and Temporal workflow progress after it; such workflows resume from their last
restored state, so their later steps may run again.

Check how far the archive is at any time:

```sh
NS=coldframe
primary() {
  kubectl -n "$NS" get pods -l cnpg.io/cluster=coldframe-db,cnpg.io/instanceRole=primary \
    -o jsonpath='{.items[0].metadata.name}'
}
kubectl -n "$NS" get clusters.postgresql.cnpg.io coldframe-db \
  -o jsonpath='{.status.conditions[?(@.type=="ContinuousArchiving")].status}{"\n"}'
kubectl -n "$NS" exec "$(primary)" -c postgres -- \
  psql -Atc "SELECT last_archived_wal, last_archived_time, last_failed_wal FROM pg_stat_archiver"
kubectl -n "$NS" get backups
```

`ContinuousArchiving` must be `True` and a recent Backup must be `completed`; a restore cannot
recover what never left the node.

## Before you start

- The S3 bucket and the Secret `coldframe-backup-s3` ([`deploy/SECRETS.md`](../../deploy/SECRETS.md)).
- cert-manager, CloudNativePG and the Barman Cloud plugin installed at the versions pinned in
  [`deploy/charts/dependencies.env`](../../deploy/charts/dependencies.env)
  ([`deploy/charts/README.md`](../../deploy/charts/README.md#prerequisites)).
- The database Secrets `coldframe-db-coldframe`, `coldframe-db-temporal` and `coldframe-db-keycloak`
  in the namespace. After the restore, CloudNativePG sets each role's password to the value in its
  Secret, so the Secrets may hold new passwords.
- The archive folder the lost cluster wrote to: its `backup.serverName`, by default the cluster
  name `coldframe-db`. It becomes `recovery.sourceServerName`. Pick a folder **never used before**
  for the restored cluster, such as `coldframe-db-2026-09-28`: Barman refuses to archive into a
  folder that already holds WAL, and sharing it would corrupt the source's timeline. The chart
  refuses to render when both are equal, but it cannot tell whether a folder was used before.

## Steps

```sh
NS=coldframe
```

1. **Stop the apps**, so that nothing writes to the old cluster or connects to the new one early:

   ```sh
   for app in server web keycloak temporal nats; do
     kubectl -n "$NS" scale deployment/$app --replicas=0
   done
   ```

2. **Optional: archive the last writes.** When the old primary still runs (for example, you
   restore because of a bad change, not a lost disk), push its current WAL segment to the bucket
   first, so the restore loses nothing. Skip this step for a point-in-time restore
   (`recovery.targetTime`).

   ```sh
   kubectl -n "$NS" exec "$(primary)" -c postgres -- psql -Atc "SELECT pg_walfile_name(pg_switch_wal())"
   # repeat until last_archived_wal is at least the segment printed above
   kubectl -n "$NS" exec "$(primary)" -c postgres -- psql -Atc "SELECT last_archived_wal FROM pg_stat_archiver"
   kubectl -n "$NS" get clusters.postgresql.cnpg.io coldframe-db \
     -o jsonpath='{.status.conditions[?(@.type=="ContinuousArchiving")].status}{"\n"}'
   ```

3. **Uninstall the database release** (when anything of it is left) and wait until the cluster,
   its pods and its volumes are gone. The backups in the object store are not touched.

   ```sh
   helm uninstall database -n "$NS" --wait
   until [ -z "$(kubectl -n "$NS" get clusters.postgresql.cnpg.io/coldframe-db -o name --ignore-not-found;
                 kubectl -n "$NS" get pods,pvc -l cnpg.io/cluster=coldframe-db -o name)" ]; do
     sleep 2
   done
   ```

4. **Install in recovery mode**, with the same object store values as before, the old folder as
   the source and a new folder for the restored cluster:

   ```sh
   helm install database deploy/charts/database -n "$NS" \
     --set backup.endpointURL=https://s3.example.org \
     --set backup.destinationPath=s3://my-bucket/coldframe/ \
     --set recovery.enabled=true \
     --set recovery.sourceServerName=coldframe-db \
     --set backup.serverName=coldframe-db-2026-09-28
   kubectl -n "$NS" wait clusters.postgresql.cnpg.io/coldframe-db --for=condition=Ready --timeout=30m
   kubectl -n "$NS" wait clusters.postgresql.cnpg.io/coldframe-db --for=condition=ContinuousArchiving --timeout=5m
   ```

   The cluster restores the latest base backup and replays the archived WAL to its end. To stop
   earlier (for example, before a bad change), add `--set recovery.targetTime=2026-09-28T10:00:00Z`.
   The ScheduledBackup takes a first base backup of the restored cluster right away.

   Then verify the database:

   ```sh
   kubectl -n "$NS" exec "$(primary)" -c postgres -- psql -Atc \
     "SELECT datname FROM pg_database WHERE datname IN ('coldframe','temporal','temporal_visibility','keycloak')"
   kubectl -n "$NS" get databases
   ```

   The four databases are listed and every Database object is applied (`APPLIED true`).

5. **Advance every Device's replay window** by a safety margin before any Device reconnects
   (AD-17). The restored cluster has lost the writes after the recovery point, including the
   latest replay counters of the Devices; without this step a Device's next messages could be
   accepted twice, or a replayed old message accepted once. **Required.** The command arrives with
   Device support (Story 4.5); until then there are no Devices (they come with Epic 4) and the step
   is a no-op.

6. **Start the apps**, NATS included: its JetStream store is disposable (AD-5), and hints kept
   from before the restore may refer to positions the database no longer has.

   ```sh
   for app in nats temporal keycloak server web; do
     kubectl -n "$NS" scale deployment/$app --replicas=1
     kubectl -n "$NS" rollout status deployment/$app --timeout=10m
   done
   ```

## Verification

- The Server is healthy:

  ```sh
  kubectl get --raw "/api/v1/namespaces/$NS/services/http:server:http/proxy/.well-known/healthz"
  ```

  answers `Healthy`.
- Keycloak serves the realm: sign in to the web app with an existing account.
- Sites, Lots and Memberships are visible: the Sites you had appear in the apps with their Lots,
  and every member still sees the Sites they belong to.
- `ContinuousArchiving` is `True`, and a Backup **created after the restore** is `completed`. Old
  Backup objects of the lost cluster may still be listed; the new one is owned by the
  ScheduledBackup and was created after step 4:

  ```sh
  kubectl -n "$NS" get backups -l cnpg.io/scheduled-backup=coldframe-db \
    --sort-by=.metadata.creationTimestamp \
    -o custom-columns=NAME:.metadata.name,CREATED:.metadata.creationTimestamp,PHASE:.status.phase
  ```

## After a restore: the next restore

Keep the recovery values in the release: the cluster now archives to the folder of step 4, and
`bootstrap` only runs when a new cluster is created, so the values change nothing on the running
one. The **next** restore takes that folder (`coldframe-db-2026-09-28`) as
`recovery.sourceServerName` and, again, a folder never used before (for example, the date of that
restore) as `backup.serverName`.

With Fleet ([`install.md`](install.md)), the recovery values go in the `database` key of the
ConfigMap `coldframe-values`, next to the object store values, and stay there:

```yaml
  database: |
    backup:
      endpointURL: https://s3.example.org
      destinationPath: s3://my-bucket/coldframe/
      serverName: coldframe-db-2026-09-28
    recovery:
      enabled: true
      sourceServerName: coldframe-db
```

Under Fleet, run the steps with the GitRepo paused, so that Fleet does not reinstall the
`database` release between steps 3 and 4: `kubectl -n fleet-local patch gitrepo coldframe
--type=merge -p '{"spec":{"paused":true}}'` before step 1. Instead of the `helm install` of step 4,
apply the ConfigMap with the recovery values above, then unpause the GitRepo and force a redeploy
(`-p '{"spec":{"paused":false,"forceSyncGeneration":<a new number>}}'`); Fleet installs the
`database` release in recovery mode, and the waits of step 4 apply unchanged. The redeploy also
brings the apps back as soon as their bundles' dependencies are Ready, so step 6 happens by itself
(scale any app still at 0 replicas); while step 5 is a no-op (no Devices before Epic 4) that order
is safe. The chart smoke tests the steps with Helm; the Fleet smoke
([`deploy/fleet/smoke.sh`](../../deploy/fleet/smoke.sh)) does not run this restore.

The old folder (`coldframe-db`) is no longer written or covered by the retention policy, which
only prunes the folder the running cluster archives to. Delete it from the bucket once the
restored cluster's first base backup has completed (see Verification), or keep it on purpose as a
frozen copy.
