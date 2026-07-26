import { createContext, useContext } from "react";

// The context and its hook live apart from ThemeContext.tsx so that file exports
// only the ThemeProvider component — a file mixing components with other exports
// breaks Fast Refresh (react-refresh/only-export-components).

export type Theme = "light" | "dark";

export const ThemeContext = createContext<{ theme: Theme; toggle: () => void }>({
  theme: "light",
  toggle: () => {},
});

export function useTheme() {
  return useContext(ThemeContext);
}
