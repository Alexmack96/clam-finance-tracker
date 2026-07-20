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
import "./index.css";
import { App } from "./App.js";
import { ThemeProvider } from "./context/ThemeContext.js";

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
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </ThemeProvider>
      </QueryClientProvider>
    </Sentry.ErrorBoundary>
  </StrictMode>,
);
