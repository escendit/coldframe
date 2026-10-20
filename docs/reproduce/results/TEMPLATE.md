# Reproduction result: YYYY-MM-DD, ‹tester›

<!--
Copy this file to docs/reproduce/results/YYYY-MM-DD-<tester>.md, fill it in, and commit it in a
pull request. Leave this template unchanged.

NO SECRETS. Do not write setup codes, keys, tokens, passwords, KEKs, Secret values, SMTP or S3
credentials, Cloudflare tokens, APNs or Firebase keys, or anything that signs in to your domain's
accounts. Your domain may be written as <domain>.
-->

Checklist: [`docs/reproduce/README.md`](../README.md).

| | |
| --- | --- |
| Tester | ‹GitHub handle›, or `author-clean` |
| Date(s) of the run | |
| Release tag | `vX.Y.Z` |
| Node hardware | ‹board, sensors, any BOM substitutes› |
| Hub hardware | ‹board› |
| Home server | ‹CPU arch, RAM, OS and version› |
| Machine the commands ran from | ‹OS and version› |
| Phones | ‹model and OS version of each› |

## Attestation

- [ ] I am not the author of Coldframe, **or** I am the author and ran on a clean machine with fresh
      hardware and no prior state (no cached toolchain, cluster, realm or enrolled boards), as in
      Part A of the checklist.
- [ ] For an author run: no Firebase project, APNs key, Cloudflare API token or S3 bucket made for
      an earlier Coldframe install was reused. (Non-author: tick only where true, otherwise n/a.)
- [ ] I used only the public repository and its docs, and got no help from the author beyond them.
- [ ] This record contains no secret.

## Per part

| Part | Pass / fail | Notes (what you saw instead, how long it took) |
| --- | --- | --- |
| A. Start clean | | |
| B. The release | | |
| C. Build the Node and the Hub | | |
| D. Flash the Hub and the Node | | |
| E. Deploy the Server at home | | |
| F. Build your own apps | | |
| G. Add the Hub and the Node, calibrate | | |
| H. A Threshold Alert inside the Notification Window | | |

## Stuck points

One row per issue filed with the "Reproduction stuck point" form during this run. Write "none" in
the first row if there were none.

| Issue | Part | Summary | Fixed by PR |
| --- | --- | --- | --- |
| | | | |

## Verdict

‹Passed / Pending doc fixes: A–H passed, issues #… still open / Failed at part ‹X›: ‹reason››
