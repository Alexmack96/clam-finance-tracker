import { defineConfig, env } from "prisma/config";
import { config } from "dotenv";

config({ path: import.meta.dirname + "/../.env" });

export default defineConfig({
  schema: "prisma/schema.prisma",
  migrations: {
    path: "prisma/migrations",
  },
  datasource: {
    url: env("DATABASE_URL"),
  },
});
