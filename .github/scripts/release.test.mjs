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
  const names = ['MazdaLink.exe', `MazdaLink-${tag}-source.zip`];
  for (const name of names) writeFileSync(join(folder, name), 'synthetic packaging test');
  const files = Object.fromEntries(names.map(name => [name, digest(name)]));
  writeFileSync(join(folder, 'release.json'), JSON.stringify({ schemaVersion: 17, version: tag.slice(1), tag, sourceCommit: sha, files, architecture: 'win-x86', applicationInterface:'WindowsForms',defaultLaunch:'desktop',terminalSwitch:'--terminal',desktopFramework:'Microsoft.WindowsDesktop.App',runtime:'10.0.14',desktopFrameworkVersion:'10.0.14', ecuProgrammingEnabled: true, programmingFamily: 'ZYJ2-ZYC3-detached-spare-EB14-EB15', readIdentityPolicy: 'reported-calibration', checksumSoftwareId: 'ZY87EC000ZYV5050', additionalChecksumSoftwareId:'ZYJ2EA000ZYV5050',zyj2EditableStart:0xC0100,zyj2EditableEndExclusive:0xE0000,modifiedRomPlan:'ZYJ2-offline-EB14-EB15-difference-only',eb15WritingImplemented:true,eb15OriginalRestoreQualification:'ZYJ2EA000ZYV5050-original-EB15-only',eb15OriginalRestoreKernelSha256:'59a08f914f848badd851ee139b416859d2b7ee80873d5ee2b53dbeb848ade4f6',eb15OriginalRestoreKernelBytes:3012,eb15OriginalRestoreHardwareAcceptance:'unqualified',eb15ColdRecoveryHardwareAcceptance:'unqualified',eb15ModifiedWritingImplemented:true,eb15ControlledRecoveryCheck:'ZYJ2EA000ZYV5050-EB15-completed-unit-zero-boundary',eb15ControlledRecoveryHardwareAcceptance:'unqualified',eb15ControlledRecoveryStopUnits:1,eb15ControlledRecoveryRequiresOriginalRestorationEvidence:true,eb15ControlledRecoveryErasesBootValidityPointer:true,eb15ControlledRecoveryResumePolicy:'exact-boundary-fresh-erase-only', benchRecoveryCheck: 'ZYJ2EA000ZYV5050-identification-only', realEcuAcceptance: 'unqualified', ramSelfTest: 'ZYJ2EA000ZYV5050-fixed-volatile-only', ramSelfTestHardwareAcceptance: 'unqualified', ramSelfTestImageSha256:'34bd2a95b30d2a34f8ccc6ccc5e3486cac2d7ecc5f8c31720e7a0a2594c1cad0', protectedFlashInitialization:'ZYJ2EA000ZYV5050-fixed-protected-only', protectedInitializationHardwareAcceptance:'unqualified', protectedInitializationImageSha256:'85ccb46064aa483d64f53b5666017b57b2624ea015fcae0299ef5f336587925b', recoveryBindingCheck:'ZYJ2EA000ZYV5050-preserved-boot-read-only', recoveryBindingHardwareAcceptance:'unqualified', recoveryBindingPreservedBytes:28928, writeEnableControl:'ZYJ2EA000ZYV5050-fixed-gpio-only', writeEnableControlHardwareAcceptance:'unqualified', writeEnableControlImageSha256:'5d6a904f85c225e12f29d171fcc1c1a88fc1bd27ac55ee5a35083b73909326c9', recoveryRehearsal:'ZYJ2EA000ZYV5050-fixed-initialization-ram-service', recoveryRehearsalHardwareAcceptance:'unqualified', recoveryRehearsalImageSha256:'3d6d56b24d9e2629418beda03b40cc436d168cd47bf33754c759a7c6a00f1623', originalRestorePlan:'ZYJ2EA000ZYV5050-original-EB14-offline-only',nativeTransactionTrial:'ZYJ2EA000ZYV5050-fixed-read-ram-stage-only',nativeTransactionHardwareAcceptance:'unqualified',nativeTransactionImageSha256:'1bc447e0e18e168767b16d7c7b1d7204b3e9d95cbcc3b8206b1d553dc8073c3f',nativeTransactionImageBytes:2036,nativeFlashCandidate:'original-only-qualification',originalRestoreTransaction:'unchanged-original-durable-intent-qualification',originalRestoreQualification:'ZYJ2EA000ZYV5050-original-EB14-only',originalRestoreHardwareAcceptance:'unqualified',originalRestoreKernelSha256:'64e9d433dfd752f28195537a97c1afe80f938c6ea9af1217f162a4a22a37d430',originalRestoreKernelBytes:3012,modifiedFlashingEnabled:true,interruptedRecoveryQualified:false,modifiedSpareFlash:'ZYJ2EA000ZYV5050-ZYC3-only',modifiedSpareRecovery:'original-both-blocks-fresh-cold-full-erase',modifiedSpareHardwareAcceptance:'unqualified',goodEcuFlashingEnabled:false,modifiedFlashBlockOrder:'EB14-then-EB15-same-monitor-session',modifiedFlashJournalSchema:1,modifiedFlashInitialImage:'supported-calibration-with-valid-checksums',controlledRecoveryCheck:'ZYJ2EA000ZYV5050-completed-unit-zero-boundary',controlledRecoveryHardwareAcceptance:'unqualified',controlledRecoveryStopUnits:1,activeFlashPowerCutImplemented:false,controlledRecoveryRequiresOriginalRestorationEvidence:true }));
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
    if (options.method === 'PATCH') { assert.equal(assets.length, 4); assert.equal(JSON.parse(options.body).draft, false); return Response.json({ id: 1 }); }
    return Response.json({ draft: true, assets: failure === 'inventory' ? [] : assets });
  };
  return { calls, request };
}
test('publishes only after all four uploaded files have matching server hashes', async () => {
  const folder = fixture(), fake = github();
  try {
    const result = await publishRelease({ repo: 'brxndon19/MazdaLink', folder, tag, commit: sha, token: 'test', notes: 'Test release', request: fake.request });
    assert.equal(result.commit, sha); assert.equal(fake.calls.at(-1).method, 'PATCH'); assert.equal(fake.calls.filter(c => c.host === 'uploads.github.com').length, 4);
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
    if (failure === 'changed executable') writeFileSync(join(folder, 'MazdaLink.exe'), 'different');
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

for (const override of [{realEcuAcceptance:'qualified'}, {benchRecoveryCheck:'generic-recovery'}, {benchRecoveryCheck:null}, {checksumSoftwareId:'ZY87EC000ZYV9999'}, {readIdentityPolicy:'fixed-target'}, {ecuProgrammingEnabled:true,programmingFamily:'Gen1'}, {ramSelfTest:'generic-upload'}, {ramSelfTestHardwareAcceptance:'qualified'}, {ramSelfTestImageSha256:'0'.repeat(64)}]) test(`rejects target/programming override ${JSON.stringify(override)}`, () => {
  const folder = fixture();
  try {
    const path = join(folder, 'release.json');
    const manifest = {...JSON.parse(readFileSync(path, 'utf8')), ...override};
    writeFileSync(path, JSON.stringify(manifest));
    writeFileSync(join(folder,'SHA256SUMS.txt'), [...Object.keys(manifest.files),'release.json'].map(name => `${createHash('sha256').update(readFileSync(join(folder,name))).digest('hex')}  ${name}`).join('\n')+'\n');
    assert.throws(()=>verifyPackage(folder,tag,sha));
  } finally {rmSync(folder,{recursive:true});}
});

for (const change of [{protectedFlashInitialization:'generic-kernel'}, {protectedInitializationHardwareAcceptance:'qualified'}, {protectedInitializationImageSha256:'0'.repeat(64)}]) {
  test(`rejects protected initialization override ${JSON.stringify(change)}`, () => {
    const folder=fixture();
    try {
      const manifest=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));
      Object.assign(manifest,change); writeFileSync(join(folder,'release.json'),JSON.stringify(manifest));
      assert.throws(()=>verifyPackage(folder,tag,sha));
    } finally {rmSync(folder,{recursive:true});}
  });
}

for (const change of [{recoveryBindingCheck:'generic-memory-reader'}, {recoveryBindingHardwareAcceptance:'qualified'}, {recoveryBindingPreservedBytes:1048576}]) {
  test(`rejects recovery binding override ${JSON.stringify(change)}`, () => {
    const folder=fixture();
    try {
      const manifest=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));
      Object.assign(manifest,change);writeFileSync(join(folder,'release.json'),JSON.stringify(manifest));
      assert.throws(()=>verifyPackage(folder,tag,sha));
    } finally {rmSync(folder,{recursive:true});}
  });
}

for (const change of [{writeEnableControl:'generic-gpio'}, {writeEnableControlHardwareAcceptance:'qualified'}, {writeEnableControlImageSha256:'0'.repeat(64)}]) {
  test(`rejects write-enable control override ${JSON.stringify(change)}`, () => {
    const folder=fixture();
    try {
      const manifest={...JSON.parse(readFileSync(join(folder,'release.json'),'utf8')),...change};
      writeFileSync(join(folder,'release.json'),JSON.stringify(manifest));
      assert.throws(()=>verifyPackage(folder,tag,sha),/Write-enable control scope/);
    } finally {rmSync(folder,{recursive:true});}
  });
}

for (const change of [{recoveryRehearsal:'generic-kernel'}, {recoveryRehearsalHardwareAcceptance:'qualified'}, {recoveryRehearsalImageSha256:'0'.repeat(64)}, {originalRestorePlan:'arbitrary-image-writer'}]) {
  test(`rejects recovery rehearsal override ${JSON.stringify(change)}`,()=>{
    const folder=fixture();try{
      const m={...JSON.parse(readFileSync(join(folder,'release.json'),'utf8')),...change};
      writeFileSync(join(folder,'release.json'),JSON.stringify(m));
      assert.throws(()=>verifyPackage(folder,tag,sha),/Recovery rehearsal scope/);
    }finally{rmSync(folder,{recursive:true});}
  });
}

for (const change of [{nativeTransactionTrial:'writer'}, {nativeTransactionHardwareAcceptance:'qualified'}, {nativeTransactionImageSha256:'0'.repeat(64)}, {nativeTransactionImageBytes:2772}, {nativeFlashCandidate:'enabled'}, {originalRestoreTransaction:'modified-rom-writer'}]) {
  test(`rejects native transaction override ${JSON.stringify(change)}`,()=>{
    const folder=fixture();try{
      const m={...JSON.parse(readFileSync(join(folder,'release.json'),'utf8')),...change};
      writeFileSync(join(folder,'release.json'),JSON.stringify(m));
      assert.throws(()=>verifyPackage(folder,tag,sha),/Native transaction scope/);
    }finally{rmSync(folder,{recursive:true});}
  });
}

for (const change of [{originalRestoreQualification:'modified-images'}, {originalRestoreHardwareAcceptance:'qualified'}, {originalRestoreKernelSha256:'0'.repeat(64)}, {originalRestoreKernelBytes:2036}, {modifiedFlashingEnabled:false}, {interruptedRecoveryQualified:true}]) {
  test(`rejects original restore override ${JSON.stringify(change)}`,()=>{
    const folder=fixture();try{
      const m={...JSON.parse(readFileSync(join(folder,'release.json'),'utf8')),...change};
      writeFileSync(join(folder,'release.json'),JSON.stringify(m));
      assert.throws(()=>verifyPackage(folder,tag,sha),/Original restore qualification scope/);
    }finally{rmSync(folder,{recursive:true});}
  });
}

for (const change of [{controlledRecoveryCheck:'arbitrary-stop'}, {controlledRecoveryHardwareAcceptance:'qualified'}, {controlledRecoveryStopUnits:2}, {activeFlashPowerCutImplemented:true}, {controlledRecoveryRequiresOriginalRestorationEvidence:false}]) {
  test(`rejects controlled recovery override ${JSON.stringify(change)}`,()=>{
    const folder=fixture();try{
      const m={...JSON.parse(readFileSync(join(folder,'release.json'),'utf8')),...change};
      writeFileSync(join(folder,'release.json'),JSON.stringify(m));
      assert.throws(()=>verifyPackage(folder,tag,sha),/Controlled recovery scope/);
    }finally{rmSync(folder,{recursive:true});}
  });
}

for (const override of [{additionalChecksumSoftwareId:'ZYJ2EA001ZYV5050'},{zyj2EditableStart:0},{zyj2EditableEndExclusive:0xF8000},{modifiedRomPlan:'flash'},{eb15WritingImplemented:false}]) test(`rejects ZYJ2 preparation override ${JSON.stringify(override)}`,()=>{
 const folder=fixture();try{
  const m=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));Object.assign(m,override);writeFileSync(join(folder,'release.json'),JSON.stringify(m));
  assert.throws(()=>verifyPackage(folder,tag,sha),/scope/);
 }finally{rmSync(folder,{recursive:true});}
});

for(const override of [{eb15OriginalRestoreQualification:'generic-flash'},{eb15OriginalRestoreKernelSha256:'0'.repeat(64)},{eb15OriginalRestoreKernelBytes:3013},{eb15OriginalRestoreHardwareAcceptance:'qualified'},{eb15ColdRecoveryHardwareAcceptance:'qualified'},{eb15ModifiedWritingImplemented:false}]) test(`rejects EB15 qualification override ${JSON.stringify(override)}`,()=>{
 const folder=fixture();try{const m=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));Object.assign(m,override);writeFileSync(join(folder,'release.json'),JSON.stringify(m));assert.throws(()=>verifyPackage(folder,tag,sha),/scope/);}finally{rmSync(folder,{recursive:true});}
});

for(const override of [{eb15ControlledRecoveryCheck:'generic-power-cut'},{eb15ControlledRecoveryHardwareAcceptance:'qualified'},{eb15ControlledRecoveryStopUnits:2},{eb15ControlledRecoveryRequiresOriginalRestorationEvidence:false},{eb15ControlledRecoveryErasesBootValidityPointer:false},{eb15ControlledRecoveryResumePolicy:'resume-unit'}]) test(`rejects EB15 boundary override ${JSON.stringify(override)}`,()=>{
 const folder=fixture();try{const m=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));Object.assign(m,override);writeFileSync(join(folder,'release.json'),JSON.stringify(m));assert.throws(()=>verifyPackage(folder,tag,sha),/scope/);}finally{rmSync(folder,{recursive:true});}
});

for (const override of [{modifiedSpareFlash:'all-Mazda'},{modifiedSpareRecovery:'resume-unit'},{modifiedSpareHardwareAcceptance:'qualified'},{goodEcuFlashingEnabled:true},{modifiedFlashBlockOrder:'EB15-first'},{modifiedFlashJournalSchema:0},{modifiedFlashInitialImage:'unchecked'}]) test(`rejects modified spare override ${JSON.stringify(override)}`,()=>{
  const dir=fixture();try{const manifest=JSON.parse(readFileSync(join(dir,'release.json'),'utf8'));Object.assign(manifest,override);writeFileSync(join(dir,'release.json'),JSON.stringify(manifest));assert.throws(()=>verifyPackage(dir,tag,sha),/scope/);}finally{rmSync(dir,{recursive:true,force:true});}
});

for (const override of [{applicationInterface:'legacy'},{defaultLaunch:'console'},{terminalSwitch:'--unsafe'},{desktopFramework:'missing'},{desktopFrameworkVersion:'0.0.0'}]) test(`rejects desktop scope override ${JSON.stringify(override)}`,()=>{
 const folder=fixture();try{const m=JSON.parse(readFileSync(join(folder,'release.json'),'utf8'));Object.assign(m,override);writeFileSync(join(folder,'release.json'),JSON.stringify(m));assert.throws(()=>verifyPackage(folder,tag,sha),/scope/);}finally{rmSync(folder,{recursive:true});}
});
