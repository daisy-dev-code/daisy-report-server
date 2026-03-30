import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAuthStore } from './stores/authStore';
import Layout from './components/Layout';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import ReportsPage from './pages/ReportsPage';
import DatasourcesPage from './pages/DatasourcesPage';
import DiscoveryPage from './pages/DiscoveryPage';
import SchedulerPage from './pages/SchedulerPage';
import PowerBiPage from './pages/PowerBiPage';
import PowerBiEmbedPage from './pages/PowerBiEmbedPage';
import UsersPage from './pages/UsersPage';
import SettingsPage from './pages/SettingsPage';

const queryClient = new QueryClient();

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuthStore();
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" />;
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            element={
              <ProtectedRoute>
                <Layout />
              </ProtectedRoute>
            }
          >
            <Route path="/" element={<DashboardPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/datasources" element={<DatasourcesPage />} />
            <Route path="/discovery" element={<DiscoveryPage />} />
            <Route path="/scheduler" element={<SchedulerPage />} />
            <Route path="/powerbi" element={<PowerBiPage />} />
            <Route path="/powerbi/embed/:workspaceId/:reportId" element={<PowerBiEmbedPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
