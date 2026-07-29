import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

function option(name, required = true) {
  const index = process.argv.indexOf(`--${name}`);
  const value = index >= 0 ? process.argv[index + 1] : undefined;
  if (required && !value) throw new Error(`Missing --${name}`);
  return value;
}

const qaPath = resolve(option('qa'));
const status = option('status');
const menpo = option('menpo');
const notes = process.argv.flatMap((argument, index) =>
  argument === '--note' && process.argv[index + 1] ? [process.argv[index + 1]] : []);

if (!['accepted', 'rejected'].includes(status)) {
  throw new Error('--status must be accepted or rejected');
}
if (!['visible', 'not-visible'].includes(menpo)) {
  throw new Error('--menpo must be visible or not-visible');
}

const qa = JSON.parse(readFileSync(qaPath, 'utf8'));
const pass = status === 'accepted' ? 'pass' : 'needs-review';
qa.selfReview = {
  status,
  reviewedAt: new Date().toISOString(),
  checks: {
    economicalStyleMatch: pass,
    edgeTreatment: pass,
    dominantHueSeparation: pass,
    anatomyAndCrop: pass,
    objectCount: pass,
    visibleMenpo: menpo === 'visible' ? pass : 'not-applicable',
    menpoEngraving: menpo === 'visible' ? 'advisory-not-gated' : 'not-applicable',
    forbiddenContent: pass,
  },
  notes,
};

writeFileSync(qaPath, `${JSON.stringify(qa, null, 2)}\n`, 'utf8');
console.log(`${status}: ${qaPath}`);
