# Releases and maintenance

## Checks and failure evidence

Every change runs locked restore, unit tests, mandatory integrity checks for every
supported editor ROM, synthetic desktop checks and standalone EXE checks.
The standalone programming check exercises Gen 1 writing and recovery with a
synthetic ECU. Physical coverage is tracked separately in [validation](VALIDATION.md).

In **Actions → CSharp checks → the failed run**, download its `checks-…` artifact.
It retains synthetic test reports and screenshots for 14 days, including files
from completed steps when a later step fails. It excludes generated ROM images,
recordings and support ZIPs. A failure before report creation may have only the
Actions log. Jobs have a 35-minute limit; newer runs replace obsolete checks for
the same branch. Pull-request branches run on PR updates; the push trigger is
limited to `main` to avoid running the same suite twice. Release checks are not
canceled by another release.

## Publish a release

Publishing an application fix includes its standalone EXE unless source-only
delivery was explicitly requested. A merge runs source checks; pushing a matching
version tag starts automatic release publication. Complete both steps and verify
the public download before calling a fix published.

1. Update the application version, matching README download and changelog in the
   release PR. Keep versioned lock metadata consistent.
2. Review the PR, pass the required checks and merge to protected `main`.
3. Create a version tag, for example `v0.6.0-alpha.5`, on that main-branch commit
   and push it. The **Release** workflow runs the full checks again.
4. The Windows job tests the standalone EXE, including crash capture and restart,
   then packages that exact file. It archives all tracked source from the
   repository root, verifies the complete path inventory, and independently
   checks every archived file against its Git blob hash at that commit.
5. The publishing job verifies that the commit belongs to `main`, checks the tag,
   version, exact file inventory and hashes, and generates GitHub build provenance.
   It creates a draft, uploads and verifies every asset, then publishes it.

Downloads include `SpeedLink.exe`, a matching source ZIP, `release.json` and
`SHA256SUMS.txt`. The EXE is not rebuilt during publishing. Provenance links the
assets to their workflow and source; it is separate from Windows code signing.
The EXE remains unsigned. To verify provenance using the GitHub CLI:

```sh
gh attestation verify SpeedLink.exe --repo mrphyslaw/SpeedLink
```

A tag must match the version and point to a commit in `main`. The workflow refuses
to replace an existing release. If an upload fails, inspect the retained draft;
delete an incomplete draft deliberately before rerunning the release job. Never
move a published tag or replace published downloads. The retention workflow keeps
the newest five published releases and preserves tags and source history; its
daily catch-up also handles workflow-created releases.

## Dependency review

Dependabot checks NuGet dependencies and pinned GitHub Actions weekly. Changes
arrive as PRs; updates must pass the required checks and be reviewed before merge.
There is no automatic merge or release.

The **Runtime maintenance** workflow checks Microsoft's official release metadata
weekly, or on demand. It considers stable patches in the current .NET runtime
line and SDK feature band. Major upgrades and feature-band changes need an
explicit decision. It updates SDK and runtime pins together, refreshes lockfiles,
runs unit/integrity checks, and uploads a tested patch with its base commit and
test results. Only after upload succeeds does it create or refresh an issue for
review. Apply the patch on a branch, review notices/documentation and open a PR;
the full desktop and standalone checks must pass before merging. Closed proposals
are respected. Open proposals are refreshed by successful weekly checks so their
14-day artifact links stay useful.

This workflow needs only source read access and permission to write review issues.
It cannot push branches, create PRs, approve reviews or publish releases. GitHub's
combined Actions PR-creation/review-approval setting stays disabled. Failed
metadata downloads, restore, tests or artifact uploads stop maintenance instead
of publishing an unchecked proposal.

Review the proposed runtime's notices, About information and documentation before
merging. Self-contained EXEs require a new release to deliver runtime fixes.
Installing a newer system runtime does not patch an already downloaded EXE.

See [GitHub provenance](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations),
[Dependabot options](https://docs.github.com/en/code-security/dependabot/working-with-dependabot/dependabot-options-reference)
and [workflow trigger rules](https://docs.github.com/en/actions/how-tos/writing-workflows/choosing-when-your-workflow-runs/triggering-a-workflow).
