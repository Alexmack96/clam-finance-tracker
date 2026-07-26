/// <reference types="vite/client" />

// Typed build-time env. Vite only exposes variables prefixed with VITE_, and both
// of these are optional — Sentry is disabled locally when they're unset.
interface ImportMetaEnv {
  readonly VITE_SENTRY_DSN?: string;
  readonly VITE_SENTRY_ENVIRONMENT?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
