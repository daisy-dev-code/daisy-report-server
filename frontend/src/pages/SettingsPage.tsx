import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import Badge from '../components/Badge';

interface ConfigEntry {
  id: number;
  configKey: string;
  configValue: string;
  category: string;
  description?: string;
  updatedAt?: string;
}

interface HealthInfo {
  status: string;
  version?: string;
  uptime?: string;
  javaVersion?: string;
  database?: string;
  freeMemory?: string;
  totalMemory?: string;
  [key: string]: unknown;
}

const categoryOrder = ['system', 'security', 'report', 'scheduler', 'cache'];
const categoryLabels: Record<string, string> = {
  system: 'System',
  security: 'Security',
  report: 'Report Engine',
  scheduler: 'Scheduler',
  cache: 'Cache',
};

export default function SettingsPage() {
  const queryClient = useQueryClient();
  const [editValues, setEditValues] = useState<Record<string, string>>({});
  const [savedKeys, setSavedKeys] = useState<Set<string>>(new Set());

  const { data: configs, isLoading } = useQuery({
    queryKey: ['system-config'],
    queryFn: () => api.get<{ data: ConfigEntry[] }>('/system/config').then(r => r.data.data),
  });

  const health = useQuery({
    queryKey: ['system-health'],
    queryFn: () => api.get<HealthInfo>('/system/health').then(r => r.data),
  });

  useEffect(() => {
    if (configs) {
      const values: Record<string, string> = {};
      for (const c of configs) {
        values[c.configKey] = c.configValue;
      }
      setEditValues(values);
    }
  }, [configs]);

  const saveMutation = useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) =>
      api.put(`/system/config/${key}`, { value }),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['system-config'] });
      setSavedKeys((prev) => new Set(prev).add(variables.key));
      setTimeout(() => {
        setSavedKeys((prev) => {
          const next = new Set(prev);
          next.delete(variables.key);
          return next;
        });
      }, 2000);
    },
  });

  function grouped(): Record<string, ConfigEntry[]> {
    if (!configs) return {};
    const groups: Record<string, ConfigEntry[]> = {};
    for (const c of configs) {
      const cat = c.category?.toLowerCase() || 'system';
      if (!groups[cat]) groups[cat] = [];
      groups[cat].push(c);
    }
    return groups;
  }

  const groups = grouped();
  const sortedCategories = Object.keys(groups).sort((a, b) => {
    const ai = categoryOrder.indexOf(a);
    const bi = categoryOrder.indexOf(b);
    return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi);
  });

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-6">Settings</h2>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Configuration */}
        <div className="lg:col-span-2 space-y-6">
          {isLoading ? (
            <div className="bg-white rounded-lg shadow p-6">
              <div className="space-y-4">
                {Array.from({ length: 6 }).map((_, i) => (
                  <div key={i} className="h-10 bg-gray-200 rounded animate-pulse" />
                ))}
              </div>
            </div>
          ) : sortedCategories.length === 0 ? (
            <div className="bg-white rounded-lg shadow p-6">
              <p className="text-gray-500">No configuration entries found.</p>
            </div>
          ) : (
            sortedCategories.map((cat) => (
              <div key={cat} className="bg-white rounded-lg shadow">
                <div className="px-6 py-4 border-b border-gray-200">
                  <h3 className="text-lg font-semibold">{categoryLabels[cat] ?? cat}</h3>
                </div>
                <div className="divide-y divide-gray-100">
                  {groups[cat].map((entry) => (
                    <div key={entry.configKey} className="px-6 py-4 flex items-center gap-4">
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium text-gray-900">{entry.configKey}</p>
                        {entry.description && (
                          <p className="text-xs text-gray-500 mt-0.5">{entry.description}</p>
                        )}
                      </div>
                      <input
                        type="text"
                        value={editValues[entry.configKey] ?? ''}
                        onChange={(e) => setEditValues({ ...editValues, [entry.configKey]: e.target.value })}
                        className="w-64 border border-gray-300 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                      />
                      <button
                        onClick={() => saveMutation.mutate({ key: entry.configKey, value: editValues[entry.configKey] ?? '' })}
                        disabled={saveMutation.isPending || editValues[entry.configKey] === entry.configValue}
                        className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 whitespace-nowrap"
                      >
                        {savedKeys.has(entry.configKey) ? 'Saved!' : 'Save'}
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>

        {/* System Info */}
        <div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-lg font-semibold mb-4">System Information</h3>
            {health.isLoading ? (
              <div className="space-y-3">
                {Array.from({ length: 4 }).map((_, i) => (
                  <div key={i} className="h-4 bg-gray-200 rounded animate-pulse" />
                ))}
              </div>
            ) : health.isError ? (
              <p className="text-red-500 text-sm">Failed to load system info.</p>
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
                {health.data?.javaVersion && (
                  <div className="flex justify-between items-center">
                    <span className="text-sm text-gray-600">Java Version</span>
                    <span className="text-sm font-medium">{health.data.javaVersion}</span>
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
                {health.data?.freeMemory && (
                  <div className="flex justify-between items-center">
                    <span className="text-sm text-gray-600">Free Memory</span>
                    <span className="text-sm font-medium">{health.data.freeMemory}</span>
                  </div>
                )}
                {health.data?.totalMemory && (
                  <div className="flex justify-between items-center">
                    <span className="text-sm text-gray-600">Total Memory</span>
                    <span className="text-sm font-medium">{health.data.totalMemory}</span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
