import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const git = (...args) => execFileSync('git', ['-C', root, ...args], {maxBuffer: 16 * 1024 * 1024}).toString();
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const files = [...new Set(git('ls-files', '-z', '--cached', '--others', '--exclude-standard')
  .split('\0').filter(Boolean))].sort().map(name => ({path: name,
    sha256: fs.existsSync(path.join(root, name)) ? sha256(fs.readFileSync(path.join(root, name))) : null}));
const data = {sourceRoot: root, head: git('rev-parse', 'HEAD').trim(), treeSha256: sha256(JSON.stringify(files)), files};
if (process.argv[3]) {
  const before = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
  if (before.treeSha256 !== data.treeSha256)
    throw new Error('Project files changed during preparation/recording. Rerun from the current main project.');
}
fs.writeFileSync(process.argv[2], JSON.stringify(data, null, 2) + '\n');
console.log(`Recording source: ${root}; tree ${data.treeSha256}`);
