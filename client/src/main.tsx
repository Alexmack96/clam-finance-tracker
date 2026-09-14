import * as Sentry from "@sentry/react";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

Sentry.init({
  dsn: import.meta.env.VITE_SENTRY_DSN,
  environment: import.meta.env.VITE_SENTRY_ENVIRONMENT,
  sendDefaultPii: true,
});
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthKitProvider } from "@workos-inc/authkit-react";
import "./index.css";
import { App } from "./App.js";
import { ThemeProvider } from "./context/ThemeContext.js";
import { AuthTokenBridge, ProvisionOnFirstSignIn } from "./components/AuthBridge.js";

const workOsClientId = import.meta.env.VITE_WORKOS_CLIENT_ID;

if (!workOsClientId) {
  // Failing here beats failing at the first API call. Without a client id
  // AuthKit cannot start a sign-in, so every request goes out unauthenticated
  // and the app looks broken in a way that points at the API rather than at a
  // missing environment variable.
  throw new Error(
    "VITE_WORKOS_CLIENT_ID is not set. Copy the Client ID from the WorkOS " +
      "dashboard (Configuration -> Client ID) into the repo-root .env.",
  );
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Every mutation that changes server data already calls
      // invalidateQueries, so cached data only goes stale via our own writes.
      // Without a staleTime the ProtectedRoute prefetch is wasted: the data
      // lands already-stale, so mounting the page refetches it immediately and
      // /api/transactions (~900 KB) is paid for twice.
      staleTime: 5 * 60 * 1000,
      // Outlive staleTime so navigating away and back stays instant.
      gcTime: 30 * 60 * 1000,
      // A 401 (expired session) or 404 will never succeed on retry.
      retry: (failureCount, error) => {
        const status = (error as { response?: { status?: number } }).response?.status;
        if (status && status >= 400 && status < 500) return false;
        return failureCount < 2;
      },
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Sentry.ErrorBoundary fallback={<p>Something went wrong.</p>}>
      {/* devMode keeps the refresh token in localStorage. Without a custom WorkOS
          auth domain it lives in a cookie on api.workos.com, which browsers block
          as third-party: silent refresh fails, requests 401 once the ~5-minute
          access token expires, and every reload signs you out. The cost is that
          script running on the page could read the token. */}
      <AuthKitProvider clientId={workOsClientId} devMode>
        <QueryClientProvider client={queryClient}>
          <AuthTokenBridge>
            <ThemeProvider>
              <BrowserRouter>
                <ProvisionOnFirstSignIn />
                <App />
              </BrowserRouter>
            </ThemeProvider>
          </AuthTokenBridge>
        </QueryClientProvider>
      </AuthKitProvider>
    </Sentry.ErrorBoundary>
  </StrictMode>,
);
