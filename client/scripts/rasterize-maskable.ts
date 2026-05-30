// One-shot: rasterize public/maskable-icon.svg → public/maskable-icon-512x512.png
// Run from client/: bun run scripts/rasterize-maskable.ts
import sharp from "sharp";
import { readFile, writeFile } from "fs/promises";
import { resolve } from "path";
import { fileURLToPath } from "url";

const here = fileURLToPath(new URL(".", import.meta.url));
const svgPath = resolve(here, "../public/maskable-icon.svg");
const outPath = resolve(here, "../public/maskable-icon-512x512.png");

const svg = await readFile(svgPath);
const png = await sharp(svg, { density: 512 })
  .resize(512, 512)
  .png()
  .toBuffer();
await writeFile(outPath, png);
console.log(`Wrote ${outPath} (${png.byteLength} bytes)`);
