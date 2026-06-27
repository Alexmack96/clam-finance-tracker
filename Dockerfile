# Stage 1: Install all monorepo dependencies
# Copy package.json files first so this layer is only invalidated when deps change
FROM oven/bun:1 AS install
WORKDIR /app
COPY package.json bun.lock* ./
COPY core/package.json ./core/
COPY server/package.json ./server/
COPY client/package.json ./client/
RUN bun install

# Stage 2: Build — generate Prisma client and compile the React frontend
FROM oven/bun:1 AS build
WORKDIR /app
COPY --from=install /app/node_modules ./node_modules
COPY --from=install /app/client/node_modules ./client/node_modules
COPY --from=install /app/server/node_modules ./server/node_modules
COPY . .
# Ensure zod is resolvable from core/ (peer dep not hoisted to root)
RUN mkdir -p core/node_modules && \
    if [ -d client/node_modules/zod ]; then \
      cp -r client/node_modules/zod core/node_modules/zod; \
    elif [ -d node_modules/zod ]; then \
      cp -r node_modules/zod core/node_modules/zod; \
    fi
RUN cd server && DATABASE_URL=file:./prisma/dev.db bunx prisma generate
RUN cd client && bunx vite build

# Stage 3: Production — lean runtime image with only what's needed to run the server
# Client source and build tooling are left behind; only client/dist and server source are included
FROM oven/bun:1 AS production
WORKDIR /app
COPY --from=install /app/node_modules ./node_modules
COPY --from=install /app/server/node_modules ./server/node_modules
COPY --from=build /app/client/dist ./client/dist
COPY --from=build /app/server ./server
COPY --from=build /app/core ./core
COPY package.json ./
ENV NODE_ENV=production
EXPOSE 3000
CMD ["sh", "-c", "cd server && bunx prisma migrate deploy && bun src/db/seed.ts && bun src/scripts/cleanup-and-backfill.ts && cd /app && bun run --cwd server src/index.ts"]
 