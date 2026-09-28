#!/usr/bin/env python3
"""Checks rendered chart manifests against the Secret contract (deploy/SECRETS.md).

    helm template ... | deploy/charts/check-manifests.py <SECRETS.md> <label>

Fails (exit 1), naming each offender, when a document is a Secret, or when a manifest references a
Secret name or key that the contract table does not list. References are secretKeyRef, envFrom
secretRef, secret volumes and projected secret sources (with their items), and imagePullSecrets. A reference
marked optional fails too: a missing Secret must stop the pod, not start it without the credential.
Needs python3 and PyYAML only.
"""

import re
import sys

import yaml

# Marks an optional Secret reference in the output of references().
OPTIONAL = object()


def read_contract(path):
    """Maps each Secret name in the contract table to the set of its keys."""
    contract = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
            if len(cells) < 3 or not line.lstrip().startswith("| `"):
                continue
            name = re.fullmatch(r"`([a-z0-9-]+)`", cells[0])
            if not name:
                continue
            contract[name.group(1)] = set(re.findall(r"`([^`]+)`", cells[2]))
    return contract


def references(node, path="$"):
    """Yields (path, secret name, key or None) for every Secret reference below node."""
    if isinstance(node, dict):
        for field, value in node.items():
            here = f"{path}.{field}"
            if field in ("secretKeyRef", "secretRef", "secret") and isinstance(value, dict) \
                    and value.get("optional") is True:
                # An optional reference would start the pod without the credential.
                yield here, OPTIONAL, None
            if field == "secretKeyRef" and isinstance(value, dict):
                yield here, value.get("name"), value.get("key")
            elif field == "secretRef" and isinstance(value, dict):
                yield here, value.get("name"), None
            elif field == "secret" and isinstance(value, dict):
                name = value.get("secretName", value.get("name"))
                items = value.get("items") or []
                if not items:
                    yield here, name, None
                for item in items:
                    yield here, name, item.get("key")
            elif field == "imagePullSecrets" and isinstance(value, list):
                for entry in value:
                    yield here, (entry or {}).get("name"), None
            else:
                yield from references(value, here)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from references(value, f"{path}[{index}]")


def main():
    if len(sys.argv) != 3:
        print(f"usage: {sys.argv[0]} <SECRETS.md> <label>", file=sys.stderr)
        return 2
    contract = read_contract(sys.argv[1])
    label = sys.argv[2]
    if not contract:
        print(f"{label}: no Secret found in the contract table of {sys.argv[1]}", file=sys.stderr)
        return 2

    errors = []
    documents = [doc for doc in yaml.safe_load_all(sys.stdin) if doc]
    for doc in documents:
        kind = doc.get("kind", "?")
        name = (doc.get("metadata") or {}).get("name", "?")
        where = f"{kind}/{name}"
        if kind == "Secret":
            errors.append(f"{where}: charts must not render a Secret")
        for path, secret, key in references(doc):
            if secret is OPTIONAL:
                errors.append(f"{where}: {path} is optional; a missing Secret must stop the pod")
            elif secret not in contract:
                errors.append(f"{where}: {path} references Secret '{secret}', which is not in SECRETS.md")
            elif key is not None and key not in contract[secret]:
                errors.append(
                    f"{where}: {path} references key '{key}' of Secret '{secret}', which is not in SECRETS.md")

    for error in errors:
        print(f"{label}: {error}", file=sys.stderr)
    if not documents:
        print(f"{label}: no documents rendered", file=sys.stderr)
        return 1
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
