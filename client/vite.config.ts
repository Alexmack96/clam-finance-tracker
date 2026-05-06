import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";
import path from "path";

export default defineConfig({
  envDir: "..",
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["icon.svg", "apple-touch-icon-180x180.png"],
      workbox: {
        maximumFileSizeToCacheInBytes: 3 * 1024 * 1024,
      },
      manifest: {
        name: "Clam Finance",
        short_name: "Clam",
        description: "Personal finance tracker",
        theme_color: "#13101f",
        background_color: "#13101f",
        display: "standalone",
        start_url: "/dashboard",
        orientation: "portrait",
        icons: [
          { src: "pwa-64x64.png",           sizes: "64x64",   type: "image/png" },
          { src: "pwa-192x192.png",          sizes: "192x192", type: "image/png" },
          { src: "pwa-512x512.png",          sizes: "512x512", type: "image/png" },
          { src: "maskable-icon-512x512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
        ],
      },
    }),
  ],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    proxy: {
      "/api": process.env.API_URL ?? "http://localhost:3000",
    },
  },
});
