import { Routes, Route, Navigate } from "react-router-dom";
import { ProtectedRoute } from "./components/ProtectedRoute.js";
import { AdminRoute } from "./components/AdminRoute.js";
import { Layout } from "./components/Layout.js";
import { LoginPage } from "./pages/LoginPage.js";
import { LoggedOutPage } from "./pages/LoggedOutPage.js";
import { DashboardPage } from "./pages/DashboardPage.js";
import { AnalyticsPage } from "./pages/AnalyticsPage.js";
import { UsersPage } from "./pages/UsersPage.js";
import { ImportPage } from "./pages/ImportPage.js";
import { SavingsPage } from "./pages/SavingsPage.js";
import { InvestmentsPage } from "./pages/InvestmentsPage.js";
import { TabsPage } from "./pages/TabsPage.js";
import { TasksPage } from "./pages/TasksPage.js";

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
          <Route element={<AdminRoute />}>
            <Route path="/users" element={<UsersPage />} />
            <Route path="/import" element={<ImportPage />} />
          </Route>
        </Route>
      </Route>
    </Routes>
  );
}
