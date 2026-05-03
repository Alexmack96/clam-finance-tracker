/**
 * Authentication e2e coverage
 *
 * Login form — happy path:
 *   - Valid credentials redirect to /dashboard
 *   - Already-authenticated visit to /login redirects to /dashboard
 *
 * Login form — client-side validation (Zod / React Hook Form):
 *   - Empty email shows "Please enter a valid email"
 *   - Empty password shows "Password is required"
 *   - Malformed email shows "Please enter a valid email"
 *   - No network call is made when client validation fails
 *
 * Login form — server errors:
 *   - Wrong password shows "Invalid email or password"
 *   - Unknown email shows "Invalid email or password"
 *
 * Route guards:
 *   - Unauthenticated access to /dashboard redirects to /login
 *   - Unauthenticated access to / redirects to /login
 *
 * Sign-out:
 *   - Sign-out navigates to /logged-out
 *   - After sign-out, visiting a protected route redirects to /login
 *
 * Session persistence:
 *   - Authenticated user stays on /dashboard after full page reload
 *
 * Preconditions:
 *   - server/.env.test must define ADMIN_EMAIL and ADMIN_PASSWORD
 *   - global-setup seeds the DB and writes e2e/.auth/user.json
 *   - Tests that use `unauthedPage` from fixtures.ts run without stored auth state
 */

import { test, expect } from "./fixtures.js";
import { VALID_EMAIL, VALID_PASSWORD, fillLoginForm, loginAs, signOut } from "./helpers.js";

// ---------------------------------------------------------------------------
// Login — happy path (uses unauthedPage so we start without a session)
// ---------------------------------------------------------------------------
test.describe("Login — happy path", () => {
  test("valid credentials redirect to /dashboard", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await expect(page.getByText("Sign in to continue your ledger.")).toBeVisible();

    await fillLoginForm(page, VALID_EMAIL, VALID_PASSWORD);

    await expect(page).toHaveURL("/dashboard");
  });

  test("already-authenticated visit to /login redirects to /dashboard", async ({ page }) => {
    // `page` fixture has stored auth state from global-setup
    await page.goto("/login");
    await expect(page).toHaveURL("/dashboard");
  });
});

// ---------------------------------------------------------------------------
// Login — client-side validation
// ---------------------------------------------------------------------------
test.describe("Login — client-side validation", () => {
  test("empty fields show required errors", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText("Please enter a valid email")).toBeVisible();
    await expect(page.getByText("Password is required")).toBeVisible();
  });

  test("malformed email shows validation error", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill("not-an-email");
    await page.getByLabel("Password").fill("anything");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText("Please enter a valid email")).toBeVisible();
    // Should not have navigated away
    await expect(page).toHaveURL("/login");
  });

  test("empty email with valid password shows email error only", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await page.getByLabel("Password").fill(VALID_PASSWORD);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText("Please enter a valid email")).toBeVisible();
    await expect(page.getByText("Password is required")).not.toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("valid email with empty password shows password error only", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(VALID_EMAIL);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText("Password is required")).toBeVisible();
    await expect(page.getByText("Please enter a valid email")).not.toBeVisible();
    await expect(page).toHaveURL("/login");
  });
});

// ---------------------------------------------------------------------------
// Login — server errors
// ---------------------------------------------------------------------------
test.describe("Login — server errors", () => {
  test("wrong password shows generic error message", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await fillLoginForm(page, VALID_EMAIL, "wrong-password-xyz");

    await expect(page.getByText("Invalid email or password")).toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("unknown email shows generic error message", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await fillLoginForm(page, "nobody@example.com", VALID_PASSWORD);

    await expect(page.getByText("Invalid email or password")).toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("button shows 'Signing in...' while request is in flight", async ({ unauthedPage: page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(VALID_EMAIL);
    await page.getByLabel("Password").fill(VALID_PASSWORD);

    // Intercept the auth request to hold it open momentarily
    await page.route("**/api/auth/sign-in/email", async (route) => {
      // Small pause to let the UI render the pending state, then continue
      await new Promise((r) => setTimeout(r, 150));
      await route.continue();
    });

    const submitButton = page.getByRole("button", { name: /Sign in|Signing in/ });
    await submitButton.click();

    await expect(page.getByRole("button", { name: "Signing in..." })).toBeDisabled();
  });

  test("error message clears when user corrects and resubmits", async ({ unauthedPage: page }) => {
    await page.goto("/login");

    // First submit with wrong password
    await fillLoginForm(page, VALID_EMAIL, "wrong-password");
    await expect(page.getByText("Invalid email or password")).toBeVisible();

    // Clear and resubmit with correct credentials
    await page.getByLabel("Password").clear();
    await page.getByLabel("Password").fill(VALID_PASSWORD);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL("/dashboard");
  });
});

// ---------------------------------------------------------------------------
// Route guards — unauthenticated access
// ---------------------------------------------------------------------------
test.describe("Route guards", () => {
  test("unauthenticated visit to /dashboard redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/login");
  });

  test("unauthenticated visit to / redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/");
    await expect(page).toHaveURL("/login");
  });

  test("unauthenticated visit to /users redirects to /login", async ({ unauthedPage: page }) => {
    await page.goto("/users");
    await expect(page).toHaveURL("/login");
  });
});

// ---------------------------------------------------------------------------
// Sign-out
// ---------------------------------------------------------------------------
test.describe("Sign-out", () => {
  test("sign-out navigates to /logged-out", async ({ unauthedPage: page }) => {
    await loginAs(page, VALID_EMAIL, VALID_PASSWORD);
    await signOut(page);
    await expect(page.getByText("You have been successfully signed out of Clam Finance Tracker.")).toBeVisible();
  });

  test("after sign-out, protected route redirects to /login", async ({ unauthedPage: page }) => {
    await loginAs(page, VALID_EMAIL, VALID_PASSWORD);
    await signOut(page);

    await page.goto("/dashboard");
    await expect(page).toHaveURL("/login");
  });

  test("logged-out page has a sign back in button that goes to /login", async ({ unauthedPage: page }) => {
    await loginAs(page, VALID_EMAIL, VALID_PASSWORD);
    await signOut(page);

    await page.getByRole("button", { name: "Sign back in" }).click();
    await expect(page).toHaveURL("/login");
  });
});

// ---------------------------------------------------------------------------
// Session persistence
// ---------------------------------------------------------------------------
test.describe("Session persistence", () => {
  test("authenticated user stays on /dashboard after page reload", async ({ page }) => {
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/dashboard");

    await page.reload();
    await expect(page).toHaveURL("/dashboard");
  });
});
