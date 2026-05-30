import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { rescueSofiChaseFx } = await import("../src/migrations/rescueSofiChaseFx.js");
await rescueSofiChaseFx();
console.log("done");
process.exit(0);
