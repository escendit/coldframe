# Source: Carbon icons

| | |
| --- | --- |
| Origin | npm package [`@carbon/icons`](https://www.npmjs.com/package/@carbon/icons), folder `svg/32/` |
| Version | 11.89.0 (`https://registry.npmjs.org/@carbon/icons/-/icons-11.89.0.tgz`) |
| Licence | Apache-2.0, full text in [`LICENSE`](LICENSE) (copied from the package) |
| Changes | None. Each file in `svg/` is a verbatim copy. |

The subset is exactly the UX-DR13 list: `rain-drop`, `checkmark--outline`, `checkmark`, `help`,
`tools`, `pause--outline`, `add`, `cloud--offline`, `overflow-menu--vertical`, `chevron--down`,
`arrow--up`, `arrow--down`, `battery--low`, `error--filled`, `view`, `in-progress`, `time`,
`grid`, `notification`, `box`, `settings`.

To add an icon: download the same package version once, copy `svg/32/<name>.svg` here, add the
name to `ICON_NAMES` in `scripts/lib/icons.ts`, and run `pnpm --filter @coldframe/design-tokens run generate`.
Nothing is downloaded at build or test time.
