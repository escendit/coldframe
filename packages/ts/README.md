# packages/ts

TypeScript libraries.

| Folder | What | Arrives in |
| --- | --- | --- |
| `api-client/` | The REST client generated from `packages/openapi` | Present as a skeleton, generated from Epic 1 on |

Every folder here with a `package.json` joins the pnpm workspace. The design tokens
([`packages/design-tokens`](../design-tokens), `@coldframe/design-tokens`) are a workspace package
too; they sit outside this folder because they generate Swift and Kotlin as well as CSS and TypeScript. Tests live in [`tests/ts`](../../tests/ts).
