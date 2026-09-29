# RKE2 configuration

Cluster-level configuration of the reference RKE2 server (RKE2 v1.36.4+rke2r1, Story 2.4). It is
not a chart: RKE2's helm-controller applies it to the charts RKE2 bundles.

| File | What |
| --- | --- |
| [`rke2-traefik-config.yaml`](rke2-traefik-config.yaml) | `HelmChartConfig` for `rke2-traefik`: removes the `web` entrypoint, so Traefik serves 443 only and nothing listens on port 80 |
| [`fleet.yaml`](fleet.yaml), [`.fleetignore`](.fleetignore) | this folder as the Fleet bundle `rke2-traefik` (Story 2.5a): with Fleet, the bundle applies the `HelmChartConfig` ([`docs/operations/install.md`](../../docs/operations/install.md)) and the manual steps below are not needed |

RKE2 v1.36.4+rke2r1 ships `rke2-traefik` 40.1.010 (pinned in
[`../charts/dependencies.env`](../charts/dependencies.env) for the checks). Its defaults open the
`web` entrypoint on hostPort 80; the `ingress` chart routes on `websecure` only
([`../charts/README.md`](../charts/README.md)).

Traefik must be RKE2's ingress controller. Check the `ingress-controller` option in
`/etc/rancher/rke2/config.yaml` (`kubectl -n kube-system get helmchart rke2-traefik` must list it);
to select Traefik explicitly, set before the first start:

```yaml
ingress-controller: traefik
```

## Apply

On the server node, as root. RKE2 applies every manifest in its manifests directory at start and
whenever the file changes:

```sh
install -m 0600 deploy/rke2/rke2-traefik-config.yaml /var/lib/rancher/rke2/server/manifests/
```

Or from any machine with cluster-admin access:

```sh
kubectl apply -f deploy/rke2/rke2-traefik-config.yaml
```

helm-controller then upgrades the `rke2-traefik` release in `kube-system` (the Job
`helm-install-rke2-traefik` runs again).

## Verify

```sh
kubectl -n kube-system get job helm-install-rke2-traefik     # COMPLETIONS 1/1
kubectl -n kube-system get svc rke2-traefik                  # PORT(S): 443/TCP only, no 80
kubectl -n kube-system get daemonset rke2-traefik \
  -o jsonpath='{range .spec.template.spec.containers[*].ports[*]}{.name}{" "}{.hostPort}{"\n"}{end}'
                                                             # websecure 443, no web / 80
kubectl -n kube-system get daemonset rke2-traefik \
  -o jsonpath='{.spec.template.spec.containers[0].args}' | tr ',' '\n' | grep -i entrypoints.web
                                                             # no output
```

And from another machine on the LAN, where `<node>` is the server's LAN address:

```sh
curl --max-time 5 http://<node>/        # must fail: connection refused
```

The checks read what is deployed: RKE2 injects values of its own into the chart, which the
repository checks do not see.

To undo, delete the file from the manifests directory (or `kubectl delete -f` it); Traefik then
opens port 80 again.
