import { mkdir, readFile, copyFile, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import { createHash } from "node:crypto";
const source = resolve(process.argv[2] ?? "");
if (!process.argv[2])
  throw new Error(
    "Usage: node tools/release/import-website-catalog.mjs <runtime export directory> [published evidence]",
  );
const root = resolve("Website/content");
const raw = await readFile(join(source, "catalog.json"));
const catalog = JSON.parse(raw);
if (
  catalog.schemaVersion !== 1 ||
  !/^\d+\.\d+\.\d+$/.test(catalog.version) ||
  !/^[a-f0-9]{40}$/.test(catalog.sourceRevision)
)
  throw new Error("Runtime catalog metadata is invalid.");
const fingerprint = createHash("sha256").update(raw).digest("hex");
if (
  (await readFile(join(source, "fingerprint.txt"), "utf8")).trim() !==
  fingerprint
)
  throw new Error("Catalog fingerprint mismatch.");
const images = new Set();
for (const models of Object.values(catalog.languages))
  for (const model of models)
    for (const field of ["image", "thumbnail"])
      if (model[field]) images.add(model[field]);
for (const path of images) {
  if (!/^images\/[a-f0-9]{64}\.webp$/.test(path))
    throw new Error("Invalid catalog image path.");
  const image = await readFile(join(source, path));
  if (createHash("sha256").update(image).digest("hex") !== path.slice(7, -5))
    throw new Error("Image hash mismatch.");
}
const destination = join(root, "versions", catalog.version);
await mkdir(destination, { recursive: true });
try {
  const prior = await readFile(join(destination, "catalog.json"));
  if (!prior.equals(raw))
    throw new Error(
      "A published version catalog is immutable. Use the next version.",
    );
} catch (error) {
  if (error.code !== "ENOENT") throw error;
}
await mkdir(join(root, "images"), { recursive: true });
for (const path of images) await copyFile(join(source, path), join(root, path));
await copyFile(join(source, "catalog.json"), join(destination, "catalog.json"));
await writeFile(join(destination, "fingerprint.txt"), fingerprint + "\n");
if (process.argv[3]) {
  const evidence = JSON.parse(await readFile(resolve(process.argv[3]), "utf8"));
  if (
    evidence.version !== catalog.version ||
    evidence.sourceRevision !== catalog.sourceRevision
  )
    throw new Error("Workshop evidence does not match this catalog.");
  if (
    evidence.workshop?.remoteChangeNoteVerified !== true ||
    !evidence.workshop?.remotePackageVerified ||
    evidence.workshop.itemId !== "3776911445"
  )
    throw new Error(
      "Workshop upload/download verification is required before promoting the current catalog.",
    );
  if (!evidence.checksums?.includes(catalog.dllSha256))
    throw new Error(
      "The exported DLL is not in the verified Workshop checksums.",
    );
  await writeFile(
    join(root, "current.json"),
    JSON.stringify(
      {
        version: catalog.version,
        sourceRevision: catalog.sourceRevision,
        fingerprint,
      },
      null,
      2,
    ) + "\n",
  );
}
console.log(
  `Imported runtime content ${catalog.version}; ${catalog.languages.zhs.length} models; ${fingerprint}`,
);
