#!/usr/bin/env python3
"""Checks the Fleet bundles of the reference GitRepo against the rules of Story 2.5a.

    fleet apply -o - coldframe <paths of gitrepo.yaml> \\
      | deploy/fleet/check-fleet.py <repo> <gitrepo.yaml> <values.example.yaml>

Reads the Bundles that `fleet apply` prints (one YAML document per `apiVersion:` line, with or
without `---` between them) on stdin. <repo> is the repository root: the GitRepo's paths are
relative to it, and <repo>/deploy/charts/dependencies.env holds the pins. Fails (exit 1), naming
each offender, when:

- the GitRepo's revision is not a release tag vX.Y.Z;
- a GitRepo path is not a folder holding a fleet.yaml (a scanned folder with files of its own
  becomes a catch-all bundle), is listed twice, or yields no bundle; or a bundle comes from no
  GitRepo path;
- a bundle has no label coldframe.escendit.io/bundle, or two bundles share its value, or a bundle
  the order below names is missing;
- a dependsOn entry names a bundle (`name`) instead of selecting it by that label alone, or its
  selector matches no bundle or more than one;
- the dependsOn graph has a cycle, or differs from the order below (an edge missing or added);
- a bundle's namespace or release name differs from the table below;
- an operator bundle's Helm repo or chart version differs from dependencies.env, or the
  cert-manager bundle lacks crds.enabled or one of the DNS-01 recursive-nameserver flags of
  docs/operations/split-dns.md;
- a Coldframe chart bundle is not the chart in its own folder, or a site-specific one does not
  read its key of the ConfigMap coldframe-values (namespace coldframe) through valuesFrom, or one
  that should not does;
- the values ConfigMap is not coldframe-values in namespace coldframe with exactly the keys the
  bundles read, each a YAML mapping;
- anything (a bundle's helm.values or a ConfigMap key) sets image.tag: the chart's appVersion, the
  release tag, is the image tag.

Needs python3 and PyYAML only.
"""

import os
import re
import sys

import yaml

LABEL = "coldframe.escendit.io/bundle"
VALUES_CONFIGMAP = ("coldframe-values", "coldframe")
RELEASE_TAG = re.compile(r"^v\d+\.\d+\.\d+$")
DNS01_FLAGS = (
    "--dns01-recursive-nameservers-only",
    "--dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53",
)

# bundle label -> (dependencies, namespace, release name)
ORDER = {
    "cert-manager": ((), "cert-manager", "cert-manager"),
    "cloudnative-pg": ((), "cnpg-system", "cnpg"),
    "barman-cloud": (("cert-manager", "cloudnative-pg"), "cnpg-system", "barman-cloud"),
    "rke2-traefik": ((), "kube-system", "rke2-traefik-config"),
    "database": (("cloudnative-pg", "barman-cloud"), "coldframe", "database"),
    "ingress": (("cert-manager", "rke2-traefik", "database"), "coldframe", "ingress"),
    "nats": (("database", "ingress"), "coldframe", "nats"),
    "temporal": (("database", "ingress"), "coldframe", "temporal"),
    "keycloak": (("database", "ingress"), "coldframe", "keycloak"),
    "server": (("nats", "temporal", "keycloak"), "coldframe", "server"),
    "web": (("server", "keycloak"), "coldframe", "web"),
}

# operator bundle -> (dependencies.env prefix, chart name)
OPERATORS = {
    "cert-manager": ("CERT_MANAGER", "cert-manager"),
    "cloudnative-pg": ("CNPG", "cloudnative-pg"),
    "barman-cloud": ("BARMAN_CLOUD_PLUGIN", "plugin-barman-cloud"),
}

COLDFRAME_CHARTS = ("database", "ingress", "nats", "temporal", "keycloak", "server", "web")
SITE_SPECIFIC = ("database", "keycloak", "server", "web", "ingress")


def load_documents(text):
    """Documents of `fleet apply -o -` output (no `---` between them) or of a plain YAML stream."""
    docs = []
    for chunk in re.split(r"(?m)^(?=apiVersion: )", text):
        docs.extend(doc for doc in yaml.safe_load_all(chunk) if doc)
    return docs


def load_env(path):
    env = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                name, value = line.split("=", 1)
                env[name] = value
    return env


def sets_image_tag(values, where):
    """Yields an error for every image.tag (at any depth, e.g. migrations.image.tag)."""
    if isinstance(values, dict):
        for key, value in values.items():
            if key == "image" and isinstance(value, dict) and "tag" in value:
                yield f"{where}: sets {key}.tag; the chart's appVersion (the release tag) is the image tag"
            yield from sets_image_tag(value, f"{where}.{key}" if where else key)


def check_gitrepo(repo, gitrepo):
    errors = []
    if (gitrepo.get("apiVersion"), gitrepo.get("kind")) != ("fleet.cattle.io/v1alpha1", "GitRepo"):
        errors.append(f"GitRepo file: expected a fleet.cattle.io/v1alpha1 GitRepo, found "
                      f"{gitrepo.get('apiVersion')} {gitrepo.get('kind')}")
    revision = (gitrepo.get("spec") or {}).get("revision")
    if not isinstance(revision, str) or not RELEASE_TAG.match(revision):
        errors.append(f"GitRepo: revision '{revision}' is not a release tag vX.Y.Z")
    paths = (gitrepo.get("spec") or {}).get("paths") or []
    if not paths:
        errors.append("GitRepo: spec.paths is empty")
    seen = set()
    for path in paths:
        if path in seen:
            errors.append(f"GitRepo path '{path}' is listed twice")
        seen.add(path)
        folder = os.path.join(repo, path)
        if not os.path.isdir(folder):
            errors.append(f"GitRepo path '{path}' is not a folder")
        elif not os.path.isfile(os.path.join(folder, "fleet.yaml")):
            errors.append(f"GitRepo path '{path}' has no fleet.yaml: Fleet would make a catch-all "
                          "bundle of its files")
    return paths, errors


def bundle_name(gitrepo_name, path):
    return f"{gitrepo_name}-{path.strip('/').replace('/', '-')}".lower()


def check_bundles(repo, gitrepo, bundles, env):
    errors = []
    gitrepo_name = (gitrepo.get("metadata") or {}).get("name", "")
    paths, gitrepo_errors = check_gitrepo(repo, gitrepo)
    errors.extend(gitrepo_errors)

    by_name = {}
    for bundle in bundles:
        name = (bundle.get("metadata") or {}).get("name", "<unnamed>")
        if bundle.get("kind") != "Bundle":
            errors.append(f"{name}: expected a Bundle, found {bundle.get('kind')}")
            continue
        by_name[name] = bundle

    expected_names = {bundle_name(gitrepo_name, path): path for path in paths}
    for name, path in expected_names.items():
        if name not in by_name:
            errors.append(f"GitRepo path '{path}' yields no bundle (expected '{name}')")
    for name in by_name:
        if name not in expected_names:
            errors.append(f"bundle '{name}' comes from no GitRepo path")

    # label value -> bundle names
    labelled = {}
    for name, bundle in by_name.items():
        value = ((bundle.get("metadata") or {}).get("labels") or {}).get(LABEL)
        if not value:
            errors.append(f"bundle '{name}' has no label {LABEL}")
            continue
        labelled.setdefault(value, []).append(name)
    for value, names in labelled.items():
        if len(names) > 1:
            errors.append(f"label {LABEL}={value} is on {len(names)} bundles: {', '.join(sorted(names))}")
        if value not in ORDER:
            errors.append(f"bundle '{names[0]}': label {LABEL}={value} is not a bundle of the order")
    for value in ORDER:
        if value not in labelled:
            errors.append(f"no bundle is labelled {LABEL}={value}")

    # The dependsOn graph, by label value.
    graph = {}
    for name, bundle in by_name.items():
        spec = bundle.get("spec") or {}
        value = ((bundle.get("metadata") or {}).get("labels") or {}).get(LABEL) or name
        edges = graph.setdefault(value, set())
        for index, entry in enumerate(spec.get("dependsOn") or []):
            where = f"bundle '{name}': dependsOn[{index}]"
            entry = entry or {}
            if entry.get("name"):
                errors.append(f"{where} names bundle '{entry['name']}'; select it by {LABEL} instead")
                continue
            selector = entry.get("selector") or {}
            match_labels = selector.get("matchLabels") or {}
            if selector.get("matchExpressions") or set(match_labels) != {LABEL}:
                errors.append(f"{where}: the selector must be matchLabels on {LABEL} only, found {selector}")
                continue
            target = match_labels[LABEL]
            matches = labelled.get(target, [])
            if len(matches) != 1:
                errors.append(f"{where}: the selector {LABEL}={target} matches {len(matches)} bundles, "
                              "expected exactly 1")
                continue
            edges.add(target)

    # Cycles (depth-first search over the label graph).
    state = {}

    def visit(node, trail):
        state[node] = "open"
        for target in sorted(graph.get(node, ())):
            if state.get(target) == "open":
                cycle = trail[trail.index(target):] + [target] if target in trail else [node, target]
                errors.append(f"dependsOn has a cycle: {' -> '.join(cycle)}")
            elif target not in state:
                visit(target, trail + [target])
        state[node] = "done"

    for node in sorted(graph):
        if node not in state:
            visit(node, [node])

    for value, (dependencies, _, _) in ORDER.items():
        if value not in graph:
            continue
        for missing in sorted(set(dependencies) - graph[value]):
            errors.append(f"bundle {value} must depend on {missing}: the order edge is missing")
        for extra in sorted(graph[value] - set(dependencies)):
            errors.append(f"bundle {value} depends on {extra}, which the order does not list")

    # Namespaces, release names, charts and values.
    for name, bundle in by_name.items():
        value = ((bundle.get("metadata") or {}).get("labels") or {}).get(LABEL)
        if value not in ORDER:
            continue
        spec = bundle.get("spec") or {}
        helm = spec.get("helm") or {}
        _, namespace, release = ORDER[value]
        if spec.get("defaultNamespace") != namespace:
            errors.append(f"bundle {value}: defaultNamespace is '{spec.get('defaultNamespace')}', expected '{namespace}'")
        if spec.get("namespace") not in (None, "", namespace):
            errors.append(f"bundle {value}: namespace is '{spec.get('namespace')}', expected '{namespace}'")
        if helm.get("releaseName") != release:
            errors.append(f"bundle {value}: helm.releaseName is '{helm.get('releaseName')}', expected '{release}'")
        errors.extend(sets_image_tag(helm.get("values") or {}, f"bundle {value}: helm.values"))

        if value in OPERATORS:
            prefix, chart = OPERATORS[value]
            if helm.get("chart") != chart:
                errors.append(f"bundle {value}: helm.chart is '{helm.get('chart')}', expected '{chart}'")
            for field, pin in (("repo", f"{prefix}_HELM_REPO"), ("version", f"{prefix}_CHART_VERSION")):
                if not env.get(pin):
                    errors.append(f"dependencies.env has no {pin}")
                elif helm.get(field) != env[pin]:
                    errors.append(f"bundle {value}: helm.{field} is '{helm.get(field)}', expected "
                                  f"'{env[pin]}' ({pin} in dependencies.env)")
            if not helm.get("takeOwnership"):
                errors.append(f"bundle {value}: helm.takeOwnership must be true to adopt a kubectl-installed operator")

        if value == "cert-manager":
            values = helm.get("values") or {}
            if ((values.get("crds") or {}).get("enabled")) is not True:
                errors.append("bundle cert-manager: helm.values.crds.enabled must be true")
            args = values.get("extraArgs") or []
            for flag in DNS01_FLAGS:
                if args.count(flag) != 1:
                    errors.append(f"bundle cert-manager: helm.values.extraArgs must hold '{flag}' once "
                                  "(docs/operations/split-dns.md)")

        if value in COLDFRAME_CHARTS:
            resources = {resource.get("name") for resource in spec.get("resources") or []}
            if helm.get("chart") or helm.get("repo") or "Chart.yaml" not in resources:
                errors.append(f"bundle {value}: expected the chart in its own folder (deploy/charts/{value})")
            refs = [(entry or {}).get("configMapKeyRef") for entry in helm.get("valuesFrom") or []]
            if value in SITE_SPECIFIC:
                want = {"name": VALUES_CONFIGMAP[0], "namespace": VALUES_CONFIGMAP[1], "key": value}
                if refs != [want]:
                    errors.append(f"bundle {value}: helm.valuesFrom must be one configMapKeyRef {want}, found {refs}")
            elif refs:
                errors.append(f"bundle {value}: has no site values and must not use helm.valuesFrom")
    return errors


def check_values(configmap):
    errors = []
    metadata = configmap.get("metadata") or {}
    if configmap.get("kind") != "ConfigMap" or (metadata.get("name"), metadata.get("namespace")) != VALUES_CONFIGMAP:
        errors.append(f"values file: expected the ConfigMap {VALUES_CONFIGMAP[0]} in namespace {VALUES_CONFIGMAP[1]}")
    data = configmap.get("data") or {}
    if set(data) != set(SITE_SPECIFIC):
        errors.append(f"values ConfigMap: keys are {sorted(data)}, expected {sorted(SITE_SPECIFIC)}")
    for key, text in data.items():
        try:
            values = yaml.safe_load(text)
        except yaml.YAMLError as error:
            errors.append(f"values ConfigMap: key {key} is not YAML: {error}")
            continue
        if not isinstance(values, dict):
            errors.append(f"values ConfigMap: key {key} is not a YAML mapping")
            continue
        errors.extend(sets_image_tag(values, f"values ConfigMap key {key}"))
    return errors


def main():
    if len(sys.argv) != 4:
        sys.exit(f"usage: {sys.argv[0]} <repo> <gitrepo.yaml> <values.example.yaml> < bundles")
    repo, gitrepo_file, values_file = sys.argv[1:]
    with open(gitrepo_file, encoding="utf-8") as handle:
        gitrepo_docs = [doc for doc in yaml.safe_load_all(handle) if doc]
    with open(values_file, encoding="utf-8") as handle:
        values_docs = [doc for doc in yaml.safe_load_all(handle) if doc]
    errors = []
    if len(gitrepo_docs) != 1:
        errors.append(f"{gitrepo_file}: expected one document, found {len(gitrepo_docs)}")
    if len(values_docs) != 1:
        errors.append(f"{values_file}: expected one document, found {len(values_docs)}")
    bundles = load_documents(sys.stdin.read())
    if not bundles:
        errors.append("no bundles on stdin")
    env = load_env(os.path.join(repo, "deploy", "charts", "dependencies.env"))
    if gitrepo_docs:
        errors.extend(check_bundles(repo, gitrepo_docs[0], bundles, env))
    if values_docs:
        errors.extend(check_values(values_docs[0]))
    if errors:
        for error in errors:
            print(f"FAIL - {error}", file=sys.stderr)
        sys.exit(1)
    print(f"ok - {len(bundles)} bundles: paths, labels, dependsOn order, pins and site values")


if __name__ == "__main__":
    main()
