#!/usr/bin/env python3
"""Checks rendered manifests for TLS on every route, DNS-01 on every ACME issuer and no port 80.

    helm template ... | deploy/charts/check-ingress.py <label>

Fails (exit 1), naming each offender, when:

- an Ingress has no `tls` entry covering one of its rule hosts, or lacks the Traefik annotations
  `traefik.ingress.kubernetes.io/router.entrypoints: websecure` (exactly) and
  `traefik.ingress.kubernetes.io/router.tls: "true"`;
- a Traefik IngressRoute (traefik.io) has no `spec.tls`, or `entryPoints` other than exactly
  `[websecure]`;
- an Issuer or ClusterIssuer with `acme` has no solver, or a solver without `dns01` or with
  `http01`;
- a Service has a port or nodePort 80, a container a containerPort or hostPort 80, or a container
  argument that configures the `web` entrypoint (`--entryPoints.web.` / `--entrypoints.web.`).

Documents of other kinds pass. An empty stream passes: most charts render no route. Needs python3
and PyYAML only.
"""

import sys

import yaml

ENTRYPOINTS = "traefik.ingress.kubernetes.io/router.entrypoints"
ROUTER_TLS = "traefik.ingress.kubernetes.io/router.tls"
CONTAINER_LISTS = ("containers", "initContainers", "ephemeralContainers")
WEB_ENTRYPOINT_ARGS = ("--entryPoints.web.", "--entrypoints.web.")


def check_ingress(doc):
    spec = doc.get("spec") or {}
    annotations = (doc.get("metadata") or {}).get("annotations") or {}
    entrypoints = [entry.strip() for entry in str(annotations.get(ENTRYPOINTS, "")).split(",")]
    if entrypoints != ["websecure"]:
        yield f"annotation {ENTRYPOINTS} is '{annotations.get(ENTRYPOINTS, '')}', expected 'websecure'"
    if str(annotations.get(ROUTER_TLS, "")) != "true":
        yield f"annotation {ROUTER_TLS} is '{annotations.get(ROUTER_TLS, '')}', expected 'true'"
    tls = spec.get("tls") or []
    if not tls:
        yield "has no tls"
    covered = {host for entry in tls for host in (entry or {}).get("hosts") or []}
    for rule in spec.get("rules") or []:
        host = (rule or {}).get("host")
        if host is None:
            yield "has a rule without a host, which no tls entry can cover"
        elif host not in covered:
            yield f"host '{host}' is not covered by tls"
    if spec.get("defaultBackend") is not None:
        yield "has a defaultBackend, which no tls entry can cover"


def check_ingressroute(doc):
    spec = doc.get("spec") or {}
    if not spec.get("tls"):
        yield "has no spec.tls"
    if spec.get("entryPoints") != ["websecure"]:
        yield f"entryPoints are {spec.get('entryPoints')}, expected ['websecure']"


def check_issuer(doc):
    acme = (doc.get("spec") or {}).get("acme")
    if acme is None:
        return
    solvers = acme.get("solvers") or []
    if not solvers:
        yield "ACME issuer has no solver"
    for index, solver in enumerate(solvers):
        solver = solver or {}
        if "http01" in solver:
            yield f"solver {index} uses http01"
        if "dns01" not in solver:
            yield f"solver {index} does not use dns01"


def check_service(doc):
    for port in (doc.get("spec") or {}).get("ports") or []:
        port = port or {}
        for field in ("port", "nodePort"):
            if port.get(field) == 80:
                yield f"port '{port.get('name', '?')}' has {field} 80"


def containers(node):
    """Yields every container below node (pod specs of any workload or template)."""
    if isinstance(node, dict):
        for field, value in node.items():
            if field in CONTAINER_LISTS and isinstance(value, list):
                yield from (item for item in value if isinstance(item, dict))
            else:
                yield from containers(value)
    elif isinstance(node, list):
        for value in node:
            yield from containers(value)


def check_containers(doc):
    for container in containers(doc):
        name = container.get("name", "?")
        for port in container.get("ports") or []:
            port = port or {}
            for field in ("containerPort", "hostPort"):
                if port.get(field) == 80:
                    yield f"container '{name}' port '{port.get('name', '?')}' has {field} 80"
        for arg in (container.get("args") or []) + (container.get("command") or []):
            if isinstance(arg, str) and arg.startswith(WEB_ENTRYPOINT_ARGS):
                yield f"container '{name}' configures the web entrypoint: {arg}"


def problems(doc):
    kind = doc.get("kind")
    api_version = str(doc.get("apiVersion", ""))
    if kind == "Ingress":
        yield from check_ingress(doc)
    elif kind == "IngressRoute" and api_version.startswith("traefik."):
        yield from check_ingressroute(doc)
    elif kind in ("Issuer", "ClusterIssuer"):
        yield from check_issuer(doc)
    elif kind == "Service":
        yield from check_service(doc)
    yield from check_containers(doc)


def main():
    if len(sys.argv) != 2:
        print(f"usage: {sys.argv[0]} <label>", file=sys.stderr)
        return 2
    label = sys.argv[1]
    errors = []
    for doc in yaml.safe_load_all(sys.stdin):
        if not isinstance(doc, dict):
            continue
        where = f"{doc.get('kind', '?')}/{(doc.get('metadata') or {}).get('name', '?')}"
        errors.extend(f"{label}: {where}: {problem}" for problem in problems(doc))
    for error in errors:
        print(error, file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
