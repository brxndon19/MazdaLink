import { readFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export function verifyPackage(folder, tag, commit) {
  const manifest = JSON.parse(readFileSync(join(folder, 'release.json'), 'utf8'));
  if (!/^v\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(tag) || !/^[a-f0-9]{40}$/.test(commit)) throw Error('Invalid tag or commit');
  if (manifest.schemaVersion !== 12 || manifest.tag !== tag || `v${manifest.version}` !== tag || manifest.sourceCommit !== commit || manifest.architecture !== 'win-x86' || manifest.ecuProgrammingEnabled !== true || manifest.programmingFamily !== 'ZYJ2-original-EB14-qualification-only' || manifest.readIdentityPolicy !== 'reported-calibration' || manifest.checksumSoftwareId !== 'ZY87EC000ZYV5050') throw Error('Release identity mismatch');
  if (manifest.benchRecoveryCheck !== 'ZYJ2EA000ZYV5050-identification-only' || manifest.realEcuAcceptance !== 'unqualified') throw Error('Bench scope mismatch');
  if (manifest.ramSelfTest !== 'ZYJ2EA000ZYV5050-fixed-volatile-only' || manifest.ramSelfTestHardwareAcceptance !== 'unqualified' || manifest.ramSelfTestImageSha256 !== '34bd2a95b30d2a34f8ccc6ccc5e3486cac2d7ecc5f8c31720e7a0a2594c1cad0') throw Error('RAM self-test scope mismatch');
  if (manifest.protectedFlashInitialization !== 'ZYJ2EA000ZYV5050-fixed-protected-only' || manifest.protectedInitializationHardwareAcceptance !== 'unqualified' || manifest.protectedInitializationImageSha256 !== '85ccb46064aa483d64f53b5666017b57b2624ea015fcae0299ef5f336587925b') throw Error('Protected initialization scope mismatch');
  if (manifest.recoveryBindingCheck !== 'ZYJ2EA000ZYV5050-preserved-boot-read-only' || manifest.recoveryBindingHardwareAcceptance !== 'unqualified' || manifest.recoveryBindingPreservedBytes !== 28928) throw Error('Recovery binding scope mismatch');
  if (manifest.writeEnableControl !== 'ZYJ2EA000ZYV5050-fixed-gpio-only' || manifest.writeEnableControlHardwareAcceptance !== 'unqualified' || manifest.writeEnableControlImageSha256 !== '5d6a904f85c225e12f29d171fcc1c1a88fc1bd27ac55ee5a35083b73909326c9') throw Error('Write-enable control scope mismatch');
  if (manifest.recoveryRehearsal !== 'ZYJ2EA000ZYV5050-fixed-initialization-ram-service' || manifest.recoveryRehearsalHardwareAcceptance !== 'unqualified' || manifest.recoveryRehearsalImageSha256 !== '3d6d56b24d9e2629418beda03b40cc436d168cd47bf33754c759a7c6a00f1623' || manifest.originalRestorePlan !== 'ZYJ2EA000ZYV5050-original-EB14-offline-only') throw Error('Recovery rehearsal scope mismatch');
  if (manifest.nativeTransactionTrial !== 'ZYJ2EA000ZYV5050-fixed-read-ram-stage-only' || manifest.nativeTransactionHardwareAcceptance !== 'unqualified' || manifest.nativeTransactionImageSha256 !== '1bc447e0e18e168767b16d7c7b1d7204b3e9d95cbcc3b8206b1d553dc8073c3f' || manifest.nativeTransactionImageBytes !== 2036 || manifest.nativeFlashCandidate !== 'original-only-qualification' || manifest.originalRestoreTransaction !== 'unchanged-original-durable-intent-qualification') throw Error('Native transaction scope mismatch');
  if (manifest.originalRestoreQualification !== 'ZYJ2EA000ZYV5050-original-EB14-only' || manifest.originalRestoreHardwareAcceptance !== 'unqualified' || manifest.originalRestoreKernelSha256 !== '64e9d433dfd752f28195537a97c1afe80f938c6ea9af1217f162a4a22a37d430' || manifest.originalRestoreKernelBytes !== 3012 || manifest.modifiedFlashingEnabled !== false || manifest.interruptedRecoveryQualified !== false) throw Error('Original restore qualification scope mismatch');
  if (manifest.controlledRecoveryCheck !== 'ZYJ2EA000ZYV5050-completed-unit-zero-boundary' || manifest.controlledRecoveryHardwareAcceptance !== 'unqualified' || manifest.controlledRecoveryStopUnits !== 1 || manifest.activeFlashPowerCutImplemented !== false || manifest.controlledRecoveryRequiresOriginalRestorationEvidence !== true) throw Error('Controlled recovery scope mismatch');
  const expected = ['MazdaLink.exe', `MazdaLink-${tag}-source.zip`];
  if (Object.keys(manifest.files).sort().join() !== [...expected].sort().join()) throw Error('Unexpected package inventory');
  const digest = name => createHash('sha256').update(readFileSync(join(folder, name))).digest('hex');
  for (const name of expected) if (digest(name) !== manifest.files[name]) throw Error(`Package hash mismatch: ${name}`);
  const sums = readFileSync(join(folder, 'SHA256SUMS.txt'), 'utf8').trim().split(/\r?\n/);
  if (sums.length !== 3 || sums.sort().join('\n') !== [...expected, 'release.json'].map(name => `${digest(name)}  ${name}`).sort().join('\n')) throw Error('Checksum inventory mismatch');
  if (readdirSync(folder).sort().join() !== [...expected, 'release.json', 'SHA256SUMS.txt'].sort().join()) throw Error('Unexpected release files');
  return manifest;
}

export async function publishRelease({ repo, folder, tag, commit, token, notes, request = fetch }) {
  if (repo !== 'brxndon19/MazdaLink') throw Error('Unexpected repository');
  const manifest = verifyPackage(folder, tag, commit);
  if (!notes?.trim()) throw Error('Release notes missing');
  const api = async (path, method = 'GET', body) => {
    const response = await request(`https://api.github.com/repos/${repo}${path}`, { method, headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'Content-Type': 'application/json', 'X-GitHub-Api-Version': '2022-11-28' }, body: body ? JSON.stringify(body) : undefined, redirect: 'error' });
    if (!response.ok) throw Error(`GitHub ${method} failed (${response.status})`);
    return response.status === 204 ? null : response.json();
  };
  const releases = await api('/releases?per_page=100');
  if (releases.some(r => r.tag_name === manifest.tag)) throw Error('Release already exists; never replace published assets');
  const release = await api('/releases', 'POST', { tag_name: manifest.tag, target_commitish: manifest.sourceCommit, name: `MazdaLink ${manifest.tag}`, draft: true, prerelease: manifest.version.includes('-'), body: `${notes}\n\nMazdaLink.exe provides calibration-unrestricted raw backups, ZY87EC000ZYV5050-only checksum preparation, a ZYJ2 spare monitor-identification check and a separately selected fixed volatile RAM self-test. The RAM experiment accepts no external code and performs no flash-controller write, erase or ROM programming. A separate protected SH7058S initialization test writes controller selection/download registers and calls only initialization entries while requiring low FWE. It changes no PD8 setting and calls no erase/program entry; the existing watchdog is serviced between phases. A separate detached-spare read-only boot-binding probe enters without normal application diagnostics and compares only the preserved boot region; it cannot upload or execute code. A separate detached-intact-spare experiment requires normal identity/RPM and live boot binding before a fixed PD8/FWE pulse and restoration check. It writes no flash-controller register and downloads no algorithm. A separate application-independent recovery rehearsal initializes both algorithms with a RAM callback and directly exercises existing timer/PB15 service while FWE stays low; it normalizes otherwise unreadable non-output port-B high-byte latches. An offline unchanged-backup EB14 plan opens no hardware. A separate detached-spare original-only qualification workflow can erase and program EB14, with durable intent, per-unit verification and matching final full reads. It is unqualified; do not intentionally interrupt this first test. A separate controlled recovery check requires completed original-restoration evidence, verifies a planned partial image after unit zero completes, prompts for a cold power cycle only at that clean boundary, and requires exact partial/preserved checks before a fresh erase and restoration. It does not cut power during an active chip operation. Modified flashing remains unavailable and active-operation power-loss recovery is unqualified. Hardware acceptance is unqualified. Exact source, SHA-256 checksums and build provenance accompany the executable.` });
  for (const name of readdirSync(folder).sort()) {
    const url = new URL(release.upload_url.split('{')[0]);
    if (url.protocol !== 'https:' || url.hostname !== 'uploads.github.com') throw Error('Unexpected upload host');
    url.searchParams.set('name', name);
    const bytes = readFileSync(join(folder, name));
    const response = await request(url, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/octet-stream' }, body: bytes, redirect: 'error' });
    if (!response.ok) throw Error(`Asset upload failed (${response.status}); draft retained`);
    const asset = await response.json();
    const expectedDigest = `sha256:${createHash('sha256').update(bytes).digest('hex')}`;
    if (asset.size !== bytes.length || asset.digest !== expectedDigest) throw Error('Uploaded asset verification failed; draft retained');
  }
  const ready = await api(`/releases/${release.id}`);
  if (ready.assets.length !== 4 || !ready.draft) throw Error('Draft inventory mismatch');
  await api(`/releases/${release.id}`, 'PATCH', { draft: false });
  return { tag: manifest.tag, commit: manifest.sourceCommit };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const tag = process.env.GITHUB_REF_NAME;
  const changelog = readFileSync('CHANGELOG.md', 'utf8').replaceAll('\r\n', '\n');
  const notes = changelog.split(`\n## ${tag.slice(1)}\n`)[1]?.split('\n## ')[0]?.trim();
  const published = await publishRelease({ repo: process.env.GITHUB_REPOSITORY, folder: process.env.RELEASE_FOLDER, tag, commit: process.env.GITHUB_SHA, token: process.env.GH_TOKEN, notes });
  console.log(`Published ${published.tag} from ${published.commit}`);
}
