# The .NET API, with the React client baked in as its static files.
#
# One image and one origin for both, which is what the Express service did and
# is the reason it could not simply be deleted once its routes were ported: it
# was also the web server. A single origin also keeps CORS out of production —
# AllowedOrigins only has to cover the Vite dev server.
#
# Build context is the REPO ROOT, not this directory, because the client and
# core/ live outside it:
#     docker build -f api-dotnet/Dockerfile .

# ── Stage 1: the React client ─────────────────────────────────────────────────
FROM oven/bun:1 AS client

WORKDIR /src
# Manifests first, so this layer is only invalidated when dependencies change.
COPY package.json bun.lock* ./
COPY core/package.json ./core/
COPY server/package.json ./server/
COPY client/package.json ./client/
RUN bun install

COPY core ./core
COPY client ./client

# Vite inlines VITE_* at build time, so these are build arguments and not
# runtime environment variables. VITE_WORKOS_CLIENT_ID is a public client id,
# not a secret — main.tsx refuses to start without it.
ARG VITE_WORKOS_CLIENT_ID
ARG VITE_SENTRY_DSN
ARG VITE_SENTRY_ENVIRONMENT=production
ENV VITE_WORKOS_CLIENT_ID=$VITE_WORKOS_CLIENT_ID \
    VITE_SENTRY_DSN=$VITE_SENTRY_DSN \
    VITE_SENTRY_ENVIRONMENT=$VITE_SENTRY_ENVIRONMENT

# `bun run build` is `tsc && vite build`. Kept whole: a type error should fail
# the image rather than ship.
RUN cd client && bun run build

# ── Stage 2: the API ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api

WORKDIR /src
# Central Package Management: both files are needed before any restore.
COPY api-dotnet/Directory.Packages.props api-dotnet/Directory.Build.props* ./
COPY api-dotnet/Clam.Api/Clam.Api.csproj ./Clam.Api/
COPY api-dotnet/Clam.ServiceDefaults/Clam.ServiceDefaults.csproj ./Clam.ServiceDefaults/
RUN dotnet restore Clam.Api/Clam.Api.csproj

COPY api-dotnet/Clam.Api ./Clam.Api
COPY api-dotnet/Clam.ServiceDefaults ./Clam.ServiceDefaults
RUN dotnet publish Clam.Api/Clam.Api.csproj -c Release -o /app/publish --no-restore

# ── Stage 3: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=api /app/publish ./
COPY --from=client /src/client/dist ./wwwroot

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Railway and most container hosts hand the port in as $PORT, which ASP.NET does
# not read — it wants ASPNETCORE_HTTP_PORTS and would otherwise sit on 8080
# while the platform routed traffic somewhere else. Translating it here means the
# image works on a host that sets PORT and on one that does not.
#
# Nothing here applies db/schema.sql. That file DROPs every table it manages, so
# running it on boot would empty the database on every deploy. It is applied by
# hand, once, deliberately — see CLAUDE.md.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet Clam.Api.dll"]
