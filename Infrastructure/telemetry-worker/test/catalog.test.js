import test from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  mkdtempSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
  cpSync,
  rmSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

test("release catalog changes names, text and art by version without rewriting history", () => {
  const root = mkdtempSync(join(tmpdir(), "ninjaslayer-catalog-"));
  const importer = fileURLToPath(
    new URL(
      "../../../tools/release/import-website-catalog.mjs",
      import.meta.url,
    ),
  );
  const content = new URL("../../../Website/content/", import.meta.url);
  const original = JSON.parse(
    readFileSync(new URL("versions/0.2.6/catalog.json", content)),
  );
  const cards = original.languages.zhs.filter(
    (model) => model.kind === "card" && model.mod,
  );
  const first = cards[0],
    alternate = cards.find((card) => card.image !== first.image);
  const source = join(root, "export");
  mkdirSync(join(source, "images"), { recursive: true });
  const catalog = {
    ...original,
    version: "8.0.0",
    languages: { zhs: [structuredClone(first)] },
  };
  const save = () => {
    const bytes = JSON.stringify(catalog);
    writeFileSync(join(source, "catalog.json"), bytes);
    writeFileSync(
      join(source, "fingerprint.txt"),
      createHash("sha256").update(bytes).digest("hex"),
    );
    for (const path of [
      catalog.languages.zhs[0].image,
      catalog.languages.zhs[0].thumbnail,
    ])
      cpSync(new URL(path, content), join(source, path));
  };
  const run = (...args) =>
    execFileSync(process.execPath, [importer, source, ...args], {
      cwd: root,
      stdio: "pipe",
    });
  try {
    save();
    run();
    const old = readFileSync(
      join(root, "Website/content/versions/8.0.0/catalog.json"),
    );
    catalog.version = "8.0.1";
    catalog.languages.zhs[0].variants[0].name = "更名演练";
    catalog.languages.zhs[0].variants[0].description = "文案同步演练。";
    catalog.languages.zhs[0].image = alternate.image;
    catalog.languages.zhs[0].thumbnail = alternate.thumbnail;
    save();
    run();
    const evidence = join(root, "evidence.json");
    writeFileSync(
      evidence,
      JSON.stringify({
        version: catalog.version,
        sourceRevision: catalog.sourceRevision,
        workshop: {
          itemId: "3776911445",
          remoteChangeNoteVerified: true,
          remotePackageVerified: true,
        },
        checksums: [catalog.dllSha256],
      }),
    );
    run(evidence);
    const current = JSON.parse(
      readFileSync(join(root, "Website/content/current.json")),
    );
    assert.equal(current.version, "8.0.1");
    assert.deepEqual(
      readFileSync(join(root, "Website/content/versions/8.0.0/catalog.json")),
      old,
    );
    const published = JSON.parse(
      readFileSync(join(root, "Website/content/versions/8.0.1/catalog.json")),
    ).languages.zhs[0];
    assert.equal(published.id, first.id);
    assert.equal(published.variants[0].name, "更名演练");
    assert.equal(published.variants[0].description, "文案同步演练。");
    assert.equal(published.image, alternate.image);
    catalog.languages.zhs[0].variants[0].name = "禁止篡改已发布版本";
    save();
    assert.throws(() => run(), /immutable/);
    catalog.version = "8.0.2";
    save();
    writeFileSync(
      join(source, catalog.languages.zhs[0].image),
      "corrupted image",
    );
    assert.throws(() => run(), /Image hash mismatch/);
    assert.deepEqual(
      JSON.parse(readFileSync(join(root, "Website/content/current.json"))),
      current,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
