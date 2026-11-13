import { readFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export function verifyPackage(folder, tag, commit) {
  const manifest = JSON.parse(readFileSync(join(folder, 'release.json'), 'utf8'));
  if (!/^v\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(tag) || !/^[a-f0-9]{40}$/.test(commit)) throw Error('Invalid tag or commit');
  if (manifest.schemaVersion !== 1 || manifest.tag !== tag || `v${manifest.version}` !== tag || manifest.sourceCommit !== commit || manifest.architecture !== 'win-x86' || manifest.ecuProgrammingEnabled !== true || manifest.programmingFamily !== 'Gen1') throw Error('Release identity mismatch');
  const expected = ['SpeedLink.exe', 'MazdaLinkReader.exe', `SpeedLink-${tag}-source.zip`];
  if (Object.keys(manifest.files).sort().join() !== [...expected].sort().join()) throw Error('Unexpected package inventory');
  const digest = name => createHash('sha256').update(readFileSync(join(folder, name))).digest('hex');
  for (const name of expected) if (digest(name) !== manifest.files[name]) throw Error(`Package hash mismatch: ${name}`);
  const sums = readFileSync(join(folder, 'SHA256SUMS.txt'), 'utf8').trim().split(/\r?\n/);
  if (sums.length !== 4 || sums.sort().join('\n') !== [...expected, 'release.json'].map(name => `${digest(name)}  ${name}`).sort().join('\n')) throw Error('Checksum inventory mismatch');
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
  const release = await api('/releases', 'POST', { tag_name: manifest.tag, target_commitish: manifest.sourceCommit, name: `MazdaLink ${manifest.tag}`, draft: true, prerelease: manifest.version.includes('-'), body: `${notes}\n\nSpeedLink.exe (desktop app) and MazdaLinkReader.exe (read-only command-line reader with checksum correction), with matching source, SHA-256 checksums and build provenance. Flashing and recovery support Gen 1 only.` });
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
  if (ready.assets.length !== 5 || !ready.draft) throw Error('Draft inventory mismatch');
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
