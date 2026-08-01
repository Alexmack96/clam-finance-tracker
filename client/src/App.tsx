import { lazy } from "react";
import { Routes, Route, Navigate } from "react-router-dom";
import { ProtectedRoute } from "./components/ProtectedRoute.js";
import { Layout } from "./components/Layout.js";
import { LoginPage } from "./pages/LoginPage.js";
import { LoggedOutPage } from "./pages/LoggedOutPage.js";

// Lazy-loaded: each page's JS (AG Grid, Recharts, etc.) is fetched only when
// its route is visited, instead of all being bundled into one upfront chunk.
const DashboardPage = lazy(() =>
  import("./pages/DashboardPage.js").then((m) => ({ default: m.DashboardPage })),
);
const AnalyticsPage = lazy(() =>
  import("./pages/AnalyticsPage.js").then((m) => ({ default: m.AnalyticsPage })),
);
const ImportPage = lazy(() =>
  import("./pages/ImportPage.js").then((m) => ({ default: m.ImportPage })),
);
const CategoriesPage = lazy(() =>
  import("./pages/CategoriesPage.js").then((m) => ({ default: m.CategoriesPage })),
);
const RulesPage = lazy(() => import("./pages/RulesPage.js"));
const SavingsPage = lazy(() =>
  import("./pages/SavingsPage.js").then((m) => ({ default: m.SavingsPage })),
);
const InvestmentsPage = lazy(() =>
  import("./pages/InvestmentsPage.js").then((m) => ({ default: m.InvestmentsPage })),
);
const TabsPage = lazy(() => import("./pages/TabsPage.js").then((m) => ({ default: m.TabsPage })));
const TasksPage = lazy(() =>
  import("./pages/TasksPage.js").then((m) => ({ default: m.TasksPage })),
);
const AdminPage = lazy(() =>
  import("./pages/AdminPage.js").then((m) => ({ default: m.AdminPage })),
);

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/logged-out" element={<LoggedOutPage />} />
      <Route element={<ProtectedRoute />}>
        <Route element={<Layout />}>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<AnalyticsPage />} />
          <Route path="/transactions" element={<DashboardPage />} />
          <Route path="/savings" element={<SavingsPage />} />
          <Route path="/investments" element={<InvestmentsPage />} />
          <Route path="/tabs" element={<TabsPage />} />
          <Route path="/tasks" element={<TasksPage />} />
          <Route path="/import" element={<ImportPage />} />
          <Route path="/categories" element={<CategoriesPage />} />
          <Route path="/rules" element={<RulesPage />} />
          <Route path="/admin" element={<AdminPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
