/**
 * Generate all PWA / app icons from public/clam-app-logo.png.
 * Run: cd client && bun run scripts/gen-icons.ts
 *
 * The source logo has wide white padding around the clam, so for app icons
 * (especially Android `maskable`, where the OS crops the corners) we center-crop
 * before resizing so the clam reads larger at small sizes. Everything is
 * flattened onto white so there's no transparency for maskable masks to expose.
 *
 * The splash (pwa-512x512.png) also gets "Clam Finance" rendered in Fraunces
 * below the logo using @napi-rs/canvas.
 */
import sharp from "sharp";
import { createCanvas, loadImage, GlobalFonts } from "@napi-rs/canvas";
import path from "path";

const PUBLIC = path.resolve(import.meta.dir, "..", "public");
const SOURCE = path.join(PUBLIC, "clam-app-logo.png");
const FONT_FILE = path.resolve(
  import.meta.dir,
  "..",
  "node_modules/@fontsource-variable/fraunces/files/fraunces-latin-full-normal.woff2"
);

/** Center-crop to `keep` fraction of the shorter side, then resize square. */
async function makeIcon(out: string, size: number, keep: number) {
  const meta = await sharp(SOURCE).metadata();
  const side = Math.floor(Math.min(meta.width!, meta.height!) * keep);
  const left = Math.floor((meta.width! - side) / 2);
  const top = Math.floor((meta.height! - side) / 2);

  await sharp(SOURCE)
    .extract({ left, top, width: side, height: side })
    .resize(size, size, { fit: "cover" })
    .flatten({ background: "#ffffff" })
    .png()
    .toFile(path.join(PUBLIC, out));

  console.log(`✓ ${out} (${size}×${size}, crop ${Math.round(keep * 100)}%)`);
}

/**
 * 512×512 splash with clam logo (cropped 85%) in the upper portion and
 * "Clam Finance" in Fraunces SemiBold below on a white background.
 */
async function makeSplash() {
  const SIZE = 512;
  const PURPLE = "#7c3aed";

  GlobalFonts.registerFromPath(FONT_FILE, "Fraunces");

  // Prepare clam: crop 85%, resize to fit upper region (leave room for text)
  const LOGO_SIZE = 320;
  const meta = await sharp(SOURCE).metadata();
  const side = Math.floor(Math.min(meta.width!, meta.height!) * 0.85);
  const left = Math.floor((meta.width! - side) / 2);
  const top = Math.floor((meta.height! - side) / 2);

  const logoBuffer = await sharp(SOURCE)
    .extract({ left, top, width: side, height: side })
    .resize(LOGO_SIZE, LOGO_SIZE, { fit: "cover" })
    .flatten({ background: "#ffffff" })
    .png()
    .toBuffer();

  const canvas = createCanvas(SIZE, SIZE);
  const ctx = canvas.getContext("2d");

  // White background
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, SIZE, SIZE);

  // Clam: start at 3% from top, giving ~33% for the text area below
  const logoX = (SIZE - LOGO_SIZE) / 2;
  const logoY = Math.round(SIZE * 0.03);
  const logoImg = await loadImage(logoBuffer);
  ctx.drawImage(logoImg, logoX, logoY, LOGO_SIZE, LOGO_SIZE);

  // "Clam Finance" — vertically centred in the remaining space below the logo
  const FONT_SIZE = 50;
  ctx.fillStyle = PURPLE;
  ctx.font = `600 ${FONT_SIZE}px Fraunces`;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  const belowLogo = logoY + LOGO_SIZE;
  const textY = belowLogo + Math.round((SIZE - belowLogo) / 2);
  ctx.fillText("Clam Finance", SIZE / 2, textY);

  const buf = canvas.toBuffer("image/png");
  await Bun.write(path.join(PUBLIC, "pwa-512x512.png"), buf);
  console.log(`✓ pwa-512x512.png (splash with text)`);
}

await makeIcon("pwa-64x64.png", 64, 0.85);
await makeIcon("pwa-192x192.png", 192, 0.85);
await makeIcon("apple-touch-icon-180x180.png", 180, 0.85);
// Maskable: Android masks to a circle/squircle and only the inner ~80% is safe.
// Keep the source's padding (clam ≈ 68% of frame) so the shell tips never clip.
await makeIcon("maskable-icon-512x512.png", 512, 1.0);
await makeSplash();

console.log("Done.");
