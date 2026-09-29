# Source: Ubuntu fonts

| File | Origin |
| --- | --- |
| `Ubuntu-Light.ttf` | `ufl/ubuntu/Ubuntu-Light.ttf` |
| `Ubuntu-Regular.ttf` | `ufl/ubuntu/Ubuntu-Regular.ttf` |
| `UbuntuCondensed-Regular.ttf` | `ufl/ubuntucondensed/UbuntuCondensed-Regular.ttf` |
| `UbuntuMono-Regular.ttf` | `ufl/ubuntumono/UbuntuMono-Regular.ttf` |
| `UFL.txt` | `ufl/ubuntu/UFL.txt` |

| | |
| --- | --- |
| Repository | [`google/fonts`](https://github.com/google/fonts) |
| Commit | `23e54b51ddffbc7713c583748e3bd86f62b1fa4a` |
| Licence | Ubuntu Font Licence 1.0, full text in [`UFL.txt`](UFL.txt) |
| Changes | None. The files are verbatim copies. |

Only weights 300 (Ubuntu Light) and 400 are used (DESIGN.md › Typography). The web app serves
these files itself through `generated/css/fonts.css`; nothing is fetched from Google Fonts.
The mobile apps register the fonts when their shells arrive.
