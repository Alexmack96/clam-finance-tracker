import sharp from "sharp";
import { readFile } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const publicDir = join(dirname(fileURLToPath(import.meta.url)), "..", "public");
const maskable = await readFile(join(publicDir, "maskable-icon.svg"));
const apple    = await readFile(join(publicDir, "apple-icon-source.svg"));

const targets = [
  { name: "apple-touch-icon-180x180.png", size: 180, source: apple },
  { name: "pwa-64x64.png",                size: 64,  source: apple },
  { name: "pwa-192x192.png",              size: 192, source: apple },
  { name: "pwa-512x512.png",              size: 512, source: apple },
  { name: "maskable-icon-512x512.png",    size: 512, source: maskable },
];

for (const { name, size, source } of targets) {
  await sharp(source, { density: 384 })
    .resize(size, size)
    .png()
    .toFile(join(publicDir, name));
  console.log(`${name} (${size}x${size})`);
}
