# Stage 1: Install all monorepo dependencies
# Copy package.json files first so this layer is only invalidated when deps change
FROM oven/bun:1 AS install
WORKDIR /app
COPY package.json bun.lock* ./
COPY core/package.json ./core/
COPY server/package.json ./server/
COPY client/package.json ./client/
RUN bun install --frozen-lockfile

# Stage 2: Build — generate Prisma client and compile the React frontend
FROM oven/bun:1 AS build
WORKDIR /app
COPY --from=install /app/node_modules ./node_modules
COPY --from=install /app/client/node_modules ./client/node_modules
COPY --from=install /app/server/node_modules ./server/node_modules
COPY . .
RUN bunx --cwd server prisma generate
RUN bun run --cwd client build

# Stage 3: Production — lean runtime image with only what's needed to run the server
# Client source and build tooling are left behind; only client/dist and server source are included
FROM oven/bun:1 AS production
WORKDIR /app
COPY --from=install /app/node_modules ./node_modules
COPY --from=install /app/server/node_modules ./server/node_modules
COPY --from=build /app/client/dist ./client/dist
COPY --from=build /app/server/node_modules/.prisma ./server/node_modules/.prisma
COPY server ./server
COPY core ./core
COPY package.json ./
ENV NODE_ENV=production
EXPOSE 3000
CMD ["sh", "-c", "bunx --cwd server prisma migrate deploy && bun run --cwd server src/index.ts"]
