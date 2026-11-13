import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { verifyPackage, publishRelease } from './release.mjs';
const sha = 'a'.repeat(40), tag = 'v0.6.0-alpha.3';
function fixture() {
  const folder = mkdtempSync(join(tmpdir(), 'SpeedLink-release-'));
  const digest = name => createHash('sha256').update(readFileSync(join(folder, name))).digest('hex');
  const names = ['SpeedLink.exe', 'MazdaLinkReader.exe', `SpeedLink-${tag}-source.zip`];
  for (const name of names) writeFileSync(join(folder, name), 'synthetic packaging test');
  const files = Object.fromEntries(names.map(name => [name, digest(name)]));
  writeFileSync(join(folder, 'release.json'), JSON.stringify({ schemaVersion: 1, version: tag.slice(1), tag, sourceCommit: sha, files, architecture: 'win-x86', ecuProgrammingEnabled: true, programmingFamily: 'Gen1' }));
  writeFileSync(join(folder, 'SHA256SUMS.txt'), [...names, 'release.json'].map(name => `${digest(name)}  ${name}`).join('\n') + '\n');
  return folder;
}
test('valid package binds source identity and exact assets', () => {
  const folder = fixture(); try { assert.equal(verifyPackage(folder, tag, sha).sourceCommit, sha); } finally { rmSync(folder, { recursive: true }); }
});
function github(failure) {
  const calls = [], assets = [];
  const request = async (address, options) => {
    const url = new URL(address); calls.push({ host: url.hostname, path: url.pathname, method: options.method });
    assert.equal(options.redirect, 'error');
    if (url.hostname === 'uploads.github.com') {
      if (failure === 'upload') return new Response('', { status: 503 });
      const name = url.searchParams.get('name'); assets.push({ name });
      return Response.json({ name, size: options.body.length, digest: failure === 'digest' ? 'sha256:wrong' : 'sha256:' + createHash('sha256').update(options.body).digest('hex') });
    }
    assert.equal(url.hostname, 'api.github.com');
    if (options.method === 'GET' && url.pathname.endsWith('/releases')) return Response.json(failure === 'existing' ? [{ tag_name: tag }] : []);
    if (options.method === 'POST') { assert.equal(JSON.parse(options.body).draft, true); return Response.json({ id: 1, upload_url: failure === 'host' ? 'https://example.com/upload' : 'https://uploads.github.com/repos/brxndon19/MazdaLink/releases/1/assets{?name,label}' }); }
    if (options.method === 'PATCH') { assert.equal(assets.length, 5); assert.equal(JSON.parse(options.body).draft, false); return Response.json({ id: 1 }); }
    return Response.json({ draft: true, assets: failure === 'inventory' ? [] : assets });
  };
  return { calls, request };
}
test('publishes only after all five uploaded files have matching server hashes', async () => {
  const folder = fixture(), fake = github();
  try {
    const result = await publishRelease({ repo: 'brxndon19/MazdaLink', folder, tag, commit: sha, token: 'test', notes: 'Test release', request: fake.request });
    assert.equal(result.commit, sha); assert.equal(fake.calls.at(-1).method, 'PATCH'); assert.equal(fake.calls.filter(c => c.host === 'uploads.github.com').length, 5);
  } finally { rmSync(folder, { recursive: true }); }
});
for (const failure of ['upload', 'digest', 'host', 'inventory', 'existing']) test(`does not publish or retry after ${failure} failure`, async () => {
  const folder = fixture(), fake = github(failure);
  try {
    await assert.rejects(publishRelease({ repo: 'brxndon19/MazdaLink', folder, tag, commit: sha, token: 'test', notes: 'Test release', request: fake.request }));
    assert.equal(fake.calls.filter(c => c.method === 'PATCH').length, 0);
    if (failure === 'upload' || failure === 'digest') assert.equal(fake.calls.filter(c => c.host === 'uploads.github.com').length, 1);
    if (failure === 'host') assert.equal(fake.calls.some(c => c.host === 'example.com'), false);
  } finally { rmSync(folder, { recursive: true }); }
});
for (const failure of ['changed executable', 'wrong commit', 'wrong tag', 'extra file', 'missing hashes', 'Gen 2 programming']) test(`rejects ${failure}`, () => {
  const folder = fixture();
  try {
    if (failure === 'changed executable') writeFileSync(join(folder, 'SpeedLink.exe'), 'different');
    if (failure === 'extra file') writeFileSync(join(folder, 'image.bin'), 'not allowed');
    if (failure === 'missing hashes') writeFileSync(join(folder, 'SHA256SUMS.txt'), '');
    if (failure === 'Gen 2 programming') {
      const manifestPath = join(folder, 'release.json');
      const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
      manifest.programmingFamily = 'Gen2';
      writeFileSync(manifestPath, JSON.stringify(manifest));
      const sums = [...Object.keys(manifest.files), 'release.json'].map(name => `${createHash('sha256').update(readFileSync(join(folder, name))).digest('hex')}  ${name}`);
      writeFileSync(join(folder, 'SHA256SUMS.txt'), sums.join('\n') + '\n');
    }
    assert.throws(() => verifyPackage(folder, failure === 'wrong tag' ? 'v0.6.0-alpha.4' : tag, failure === 'wrong commit' ? 'b'.repeat(40) : sha));
  } finally { rmSync(folder, { recursive: true }); }
});
