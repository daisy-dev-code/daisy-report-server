import { useQuery } from '@tanstack/react-query';
import api from '../api/client';
import Badge from '../components/Badge';

interface HealthInfo {
  status: string;
  version?: string;
  uptime?: string;
  database?: string;
  [key: string]: unknown;
}

interface AuditEntry {
  id: number;
  action: string;
  username?: string;
  entityType?: string;
  entityName?: string;
  timestamp: string;
  details?: string;
}

interface PaginatedResponse<T> {
  items: T[];
  totalItems: number;
}

export default function DashboardPage() {
  const reports = useQuery({
    queryKey: ['dashboard-reports'],
    queryFn: () => api.get<PaginatedResponse<unknown>>('/reports', { params: { page: 1, pageSize: 1 } }).then(r => r.data.totalItems),
  });

  const datasources = useQuery({
    queryKey: ['dashboard-datasources'],
    queryFn: () => api.get<PaginatedResponse<unknown>>('/datasources', { params: { page: 1, pageSize: 1 } }).then(r => r.data.totalItems),
  });

  const jobs = useQuery({
    queryKey: ['dashboard-jobs'],
    queryFn: () => api.get<PaginatedResponse<unknown>>('/scheduler/jobs', { params: { page: 1, pageSize: 1 } }).then(r => r.data.totalItems),
  });

  const users = useQuery({
    queryKey: ['dashboard-users'],
    queryFn: () => api.get<PaginatedResponse<unknown>>('/users', { params: { page: 1, pageSize: 1 } }).then(r => r.data.totalItems),
  });

  const audit = useQuery({
    queryKey: ['dashboard-audit'],
    queryFn: () => api.get<PaginatedResponse<AuditEntry>>('/audit', { params: { pageSize: 10 } }).then(r => r.data.items ?? r.data),
  });

  const health = useQuery({
    queryKey: ['dashboard-health'],
    queryFn: () => api.get<HealthInfo>('/system/health').then(r => r.data),
  });

  const stats = [
    { label: 'Reports', value: reports.data, color: 'bg-blue-500', loading: reports.isLoading },
    { label: 'Datasources', value: datasources.data, color: 'bg-green-500', loading: datasources.isLoading },
    { label: 'Scheduled Jobs', value: jobs.data, color: 'bg-purple-500', loading: jobs.isLoading },
    { label: 'Users', value: users.data, color: 'bg-orange-500', loading: users.isLoading },
  ];

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-6">Dashboard</h2>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {stats.map((stat) => (
          <div key={stat.label} className="bg-white rounded-lg shadow p-6">
            <div className={`w-10 h-10 ${stat.color} rounded-lg mb-3`} />
            <p className="text-sm text-gray-500">{stat.label}</p>
            {stat.loading ? (
              <div className="h-9 w-16 bg-gray-200 rounded animate-pulse mt-1" />
            ) : (
              <p className="text-3xl font-bold">{stat.value ?? 0}</p>
            )}
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* System Health */}
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold mb-4">System Health</h3>
          {health.isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 3 }).map((_, i) => (
                <div key={i} className="h-4 bg-gray-200 rounded animate-pulse" />
              ))}
            </div>
          ) : health.isError ? (
            <p className="text-red-500 text-sm">Failed to load health status.</p>
          ) : (
            <div className="space-y-3">
              <div className="flex justify-between items-center">
                <span className="text-sm text-gray-600">Status</span>
                <Badge
                  text={health.data?.status ?? 'Unknown'}
                  variant={health.data?.status === 'UP' || health.data?.status === 'healthy' ? 'success' : 'error'}
                />
              </div>
              {health.data?.version && (
                <div className="flex justify-between items-center">
                  <span className="text-sm text-gray-600">Version</span>
                  <span className="text-sm font-medium">{health.data.version}</span>
                </div>
              )}
              {health.data?.uptime && (
                <div className="flex justify-between items-center">
                  <span className="text-sm text-gray-600">Uptime</span>
                  <span className="text-sm font-medium">{health.data.uptime}</span>
                </div>
              )}
              {health.data?.database && (
                <div className="flex justify-between items-center">
                  <span className="text-sm text-gray-600">Database</span>
                  <Badge
                    text={health.data.database}
                    variant={health.data.database === 'UP' || health.data.database === 'connected' ? 'success' : 'error'}
                  />
                </div>
              )}
            </div>
          )}
        </div>

        {/* Recent Activity */}
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold mb-4">Recent Activity</h3>
          {audit.isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <div key={i} className="h-4 bg-gray-200 rounded animate-pulse" />
              ))}
            </div>
          ) : audit.isError ? (
            <p className="text-gray-400 text-sm">No recent activity available.</p>
          ) : (
            <div className="space-y-3">
              {(Array.isArray(audit.data) ? audit.data : []).length === 0 ? (
                <p className="text-gray-400 text-sm">No recent activity.</p>
              ) : (
                (Array.isArray(audit.data) ? audit.data : []).slice(0, 10).map((entry) => (
                  <div key={entry.id} className="flex items-start gap-3 text-sm border-b border-gray-100 pb-2 last:border-0">
                    <div className="flex-1">
                      <span className="font-medium">{entry.username ?? 'System'}</span>{' '}
                      <span className="text-gray-600">{entry.action}</span>
                      {entry.entityName && (
                        <span className="text-gray-500"> - {entry.entityType}: {entry.entityName}</span>
                      )}
                    </div>
                    <span className="text-xs text-gray-400 whitespace-nowrap">
                      {new Date(entry.timestamp).toLocaleString()}
                    </span>
                  </div>
                ))
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
