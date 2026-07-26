import js from "@eslint/js";
import tseslint from "@typescript-eslint/eslint-plugin";
import tsparser from "@typescript-eslint/parser";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import prettier from "eslint-config-prettier";
import globals from "globals";

export default [
  {
    ignores: ["dist/**", "dev-dist/**", "src/components/ui/**"],
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    languageOptions: {
      parser: tsparser,
      ecmaVersion: 2022,
      sourceType: "module",
      globals: { ...globals.browser },
      parserOptions: {
        ecmaFeatures: { jsx: true },
      },
    },
    plugins: {
      "@typescript-eslint": tseslint,
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      ...js.configs.recommended.rules,
      ...tseslint.configs.recommended.rules,
      ...reactHooks.configs.recommended.rules,
      // TypeScript checks identifiers itself; no-undef false-positives on type-only refs (e.g. React.ReactNode).
      "no-undef": "off",
      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      "@typescript-eslint/explicit-function-return-type": "off",
      // Allow intentional `any` (used heavily with ag-grid/recharts generics) but flag the rest.
      "@typescript-eslint/no-explicit-any": "off",
      // Reports components the React Compiler would skip optimising (React Hook
      // Form's form.watch(), TanStack Virtual's useVirtualizer()). This project
      // doesn't run the React Compiler — there's no babel-plugin-react-compiler in
      // vite.config.ts — so it's advisory noise about an optimisation we never do,
      // for two libraries we've deliberately chosen. Not a correctness rule.
      "react-hooks/incompatible-library": "off",
    },
  },
  {
    files: ["src/**/*.test.{ts,tsx}", "src/test/**"],
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
    },
  },
  // Disable ESLint rules that conflict with Prettier — keep this last.
  prettier,
];
