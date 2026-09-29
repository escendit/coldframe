# Source: Escendit branding theme

| | |
| --- | --- |
| Origin | [`escendit/branding`](https://github.com/escendit/branding), file `css/theme.css` |
| Commit | `67c33f39c49e118be6785d11ebdea1f3bc5191f8` |
| Licence | Apache-2.0 (`license` in the repository's `package.json`; full text in [`LICENSE`](LICENSE)) |
| Changes | None. `theme.css` is a verbatim copy. |

Coldframe does not depend on the `@escendit/branding` package. The copy here is the input the
token tests read to prove that `tokens/tokens.json` still matches the Escendit Design System.

To update: copy `css/theme.css` from a newer commit, change the commit above, run
`pnpm --filter @coldframe/design-tokens-tests test` and fix every token the provenance test names.
