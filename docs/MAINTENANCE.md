# MazdaLink releases and maintenance

The current product is `MazdaLink.exe`, a dedicated terminal application for exactly
`ZY87EC000ZYV5050`. The release pipeline does not compile or ship the archived
SpeedLink desktop/reader. It runs focused target/transport/menu/integrity tests,
the complete retained core regression and all-profile ROM-save integrity gates,
and standalone console checks. Historical GUI checks remain a manual legacy workflow.

Bump the version and changelog, pass the PR checks, merge to main, then push its
matching version tag. The tag runs checks again and packages the exact tested EXE.
The source archive contains every tracked file with its Git blob hash verified.
`release.json` binds the exact commit, Windows x86 architecture, target software,
disabled programming status, SDK/runtime pins, EXE and source hashes. Publication
verifies the fixed four-file inventory, generates build provenance, verifies server
asset digests and publishes. Confirm public downloads and hashes before reporting completion.
Never move a published tag or replace its files.

The package contains `MazdaLink.exe`, `MazdaLink-v<version>-source.zip`, `release.json`
and `SHA256SUMS.txt`. GPL notices/licence are embedded and included in source.
No ROMs, vendor drivers or private research are included. Synthetic reports are retained
for 14 days, including failure evidence; generated `.rom`/capture files are excluded.
Physical ECU/adapter qualification is separate. The app has no enabled programming path.

Runtime/dependency pins and protected integrity checks remain mandatory. Updates require
review; no dependency automation may merge or publish. The existing runtime maintenance
and release retention workflows remain in place. To verify provenance:

```sh
gh attestation verify MazdaLink.exe --repo brxndon19/MazdaLink
```
