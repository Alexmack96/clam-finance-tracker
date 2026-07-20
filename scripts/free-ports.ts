#!/usr/bin/env bun
// Force-kills whatever is listening on the given ports before a dev server
// starts, so orphaned bun/vite processes from a previous session never push
// us onto an alternate port (e.g. 5174, 5175). Wired as `predev` in root,
// client, and server package.json — each with the port(s) it owns — so this
// runs regardless of whether dev is started via the root orchestrator or
// directly inside client/ or server/ (the two-terminal workflow in CLAUDE.md).
import { execSync } from "node:child_process";

const ports = process.argv.slice(2).map(Number).filter(Boolean);
const isWindows = process.platform === "win32";

for (const port of ports) {
  try {
    if (isWindows) {
      const out = execSync(
        `powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess"`,
        { encoding: "utf8" },
      ).trim();
      const pids = [...new Set(out.split(/\s+/).filter(Boolean))];
      for (const pid of pids) {
        execSync(`taskkill /PID ${pid} /F`, { stdio: "ignore" });
        console.log(`[free-ports] killed PID ${pid} on port ${port}`);
      }
    } else {
      const out = execSync(`lsof -ti tcp:${port}`, { encoding: "utf8" }).trim();
      for (const pid of out.split("\n").filter(Boolean)) {
        execSync(`kill -9 ${pid}`);
        console.log(`[free-ports] killed PID ${pid} on port ${port}`);
      }
    }
  } catch {
    // Nothing was listening on this port — nothing to clean up.
  }
}
