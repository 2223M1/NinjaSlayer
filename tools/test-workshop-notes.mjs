import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync, chmodSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const temporary = mkdtempSync(join(tmpdir(), 'ninjaslayer-workshop-note-'));
const bash = process.env.NINJASLAYER_TEST_BASH ?? (process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash');
const shellPath = value => value.replaceAll('\\', '/');
try {
  const content = join(temporary, 'content');
  mkdirSync(join(content, 'lib'), { recursive: true });
  for (const name of ['NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'ninjaslayer-variants.manifest', 'SHA256SUMS']) {
    writeFileSync(join(content, name), 'fixture');
  }
  const preview = join(temporary, 'image.png');
  const noteFile = join(temporary, 'note.md');
  const capture = join(temporary, 'captured.vdf');
  const steam = join(temporary, 'steamcmd');
  writeFileSync(preview, 'fixture');
  writeFileSync(steam, '#!/usr/bin/env bash\nset -eu\nif [[ "$3" == "+workshop_build_item" ]]; then cp "$4" "$CAPTURE"; fi\n');
  chmodSync(steam, 0o755);
  const run = () => spawnSync(bash, [shellPath(join(root, '.github/scripts/publish-workshop.sh'))], {
    cwd: root, encoding: 'utf8', env: { ...process.env,
      STEAM_USERNAME: 'fixture', STEAM_CONFIG_VDF: 'not-a-credential',
      CONTENT_DIR: shellPath(content), PREVIEW_FILE: shellPath(preview),
      STEAMCMD: shellPath(steam), STEAM_HOME: shellPath(join(temporary, 'steam-home')),
      WORKSHOP_ITEM_ID: '3776911445', WORKSHOP_VISIBILITY: '0',
      RELEASE_NOTE_FILE: shellPath(noteFile), CAPTURE: shellPath(capture),
    },
  });
  const note = 'chop+：7伤害1空手道调整为5伤害2空手道。\n修复了用户反馈中龙卷卡回合的问题。\n说明："茶道"与路径A\\B。';
  writeFileSync(noteFile, note.replaceAll('\n', '\r\n') + '\r\n');
  let result = run();
  assert.equal(result.status, 0, result.stderr + result.stdout);
  const manifest = readFileSync(capture, 'utf8');
  const encoded = manifest.match(/"changenote" "((?:\\.|[^"\\])*)"/s)?.[1];
  assert.ok(encoded, 'SteamCMD must receive the approved note');
  assert.equal(encoded.replace(/\\([\\"n])/g, (_, c) => c === 'n' ? '\n' : c), note);
  assert.ok(!manifest.includes('"title"') && !manifest.includes('"description"'), 'Note transport must preserve localized metadata');
  const before = manifest;
  writeFileSync(noteFile, ' \r\n\t');
  result = run();
  assert.notEqual(result.status, 0, 'Blank notes must stop before calling SteamCMD');
  assert.match(result.stdout + result.stderr, /must not be empty/);
  assert.equal(readFileSync(capture, 'utf8'), before);
  console.log('Workshop note transport passed: Chinese, CRLF, multiline, quotes, backslashes and blank-note rejection.');
} finally {
  if (dirname(resolve(temporary)) !== resolve(tmpdir()) || !basename(temporary).startsWith('ninjaslayer-workshop-note-')) {
    throw new Error('Unexpected fixture directory');
  }
  if (existsSync(temporary)) rmSync(temporary, { recursive: true });
}
