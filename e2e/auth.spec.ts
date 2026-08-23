/**
 * Route guards.
 *
 * This file used to hold the whole authentication suite: the login form's happy
 * path and client-side validation, its server errors, sign-out, and session
 * persistence. All of it drove an email-and-password form that Better Auth
 * served, and all of it went when the client moved to WorkOS AuthKit — the form
 * is gone (see the note at the top of `client/src/pages/LoginPage.tsx`), so
 * every one of those tests was asserting against a page that no longer renders.
 *
 * What survives is the part that never depended on *how* you sign in: an
 * unauthenticated visit to a protected route lands on /login. That is as true
 * under AuthKit as it was under Better Auth, and these three still pass.
 *
 * **Coverage deliberately left missing**, for whoever rewrites this against
 * AuthKit:
 *
 *   - Signing in at all, and landing on /dashboard afterwards.
 *   - Sign-out, /logged-out, and the sign-back-in button.
 *   - A session surviving a full page reload.
 *   - An already-authenticated visit to /login redirecting to /dashboard.
 *
 * Those need a real AuthKit session in the browser, which `e2e/global-setup.ts`
 * cannot currently produce — it still signs in through Better Auth's
 * `/api/auth/sign-in/email` and saves a cookie the client no longer reads. That
 * stale session is also why the smoke, Monzo and transactions-rendering specs
 * fail: they are not broken, they are unauthenticated.
 */

import { test, expect } from "./fixtures.js";

test.describe("Route guards", () => {
  test("unauthenticated visit to /dashboard redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/login");
  });

  test("unauthenticated visit to / redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/");
    await expect(page).toHaveURL("/login");
  });

  // A protected route that isn't the dashboard, so the guard is shown to cover more
  // than the default landing page. Was /users until `feat: remove admin` deleted that
  // route — the test kept passing a non-existent path and failed every run after.
  test("unauthenticated visit to /import redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/import");
    await expect(page).toHaveURL("/login");
  });
});
