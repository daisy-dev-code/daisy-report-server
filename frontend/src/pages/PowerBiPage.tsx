import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

/* ───────── Types ───────── */

interface PbiConfig {
  tenantId: string;
  clientId: string;
  clientSecret: string;
  connected: boolean;
}

interface Workspace {
  id: string;
  name: string;
  type: string;
  state: string;
  isOnDedicatedCapacity: boolean;
}

interface Report {
  id: string;
  name: string;
  workspaceId: string;
  workspaceName?: string;
  reportType: string;
  datasetId?: string;
  modifiedDateTime?: string;
}

interface Dataset {
  id: string;
  name: string;
  workspaceId: string;
  workspaceName?: string;
  isRefreshable: boolean;
  configuredBy?: string;
}

interface RefreshEntry {
  id: number;
  requestId?: string;
  status: string;
  startTime: string;
  endTime?: string;
}

interface SyncHistoryEntry {
  id: number;
  type: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  itemsSynced?: number;
  errorMessage?: string;
}

/* ───────── Helpers ───────── */

type Tab = 'config' | 'workspaces' | 'reports' | 'datasets' | 'sync';

function formatDateTime(val?: string) {
  if (!val) return '-';
  return new Date(val).toLocaleString();
}

function statusVariant(status: string): 'success' | 'warning' | 'error' | 'info' {
  switch (status?.toUpperCase()) {
    case 'ACTIVE': case 'COMPLETED': case 'SUCCEEDED': case 'CONNECTED': return 'success';
    case 'RUNNING': case 'IN_PROGRESS': case 'UNKNOWN': return 'warning';
    case 'FAILED': case 'ERROR': case 'DISABLED': return 'error';
    default: return 'info';
  }
}

const tabClasses = (active: boolean) =>
  `px-4 py-2 text-sm font-medium rounded-t-lg border-b-2 transition-colors ${
    active
      ? 'border-blue-600 text-blue-600 bg-white'
      : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
  }`;

const inputCls = 'w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500';
const btnPrimary = 'px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50';
const btnSecondary = 'px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50';

/* ───────── Component ───────── */

export default function PowerBiPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>('config');
  const [workspaceFilter, setWorkspaceFilter] = useState('');
  const [reportSearch, setReportSearch] = useState('');
  const [datasetSearch, setDatasetSearch] = useState('');

  /* Config form */
  const [configForm, setConfigForm] = useState({ tenantId: '', clientId: '', clientSecret: '' });
  const [configLoaded, setConfigLoaded] = useState(false);

  /* Modals */
  const [refreshHistoryDataset, setRefreshHistoryDataset] = useState<Dataset | null>(null);
  const [exportingReport, setExportingReport] = useState<Report | null>(null);

  /* ── Config queries ── */
  const configQuery = useQuery({
    queryKey: ['pbi-config'],
    queryFn: () => api.get<PbiConfig>('/powerbi/config').then(r => r.data),
    retry: false,
  });

  // Populate form from server once
  if (configQuery.data && !configLoaded) {
    setConfigForm({
      tenantId: configQuery.data.tenantId ?? '',
      clientId: configQuery.data.clientId ?? '',
      clientSecret: '', // never echoed back
    });
    setConfigLoaded(true);
  }

  const saveConfig = useMutation({
    mutationFn: () => api.post('/powerbi/config', configForm),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['pbi-config'] }),
  });

  const testConnection = useMutation({
    mutationFn: () => api.post<{ success: boolean; message?: string }>('/powerbi/config/test', configForm),
  });

  /* ── Workspaces ── */
  const workspacesQuery = useQuery({
    queryKey: ['pbi-workspaces'],
    queryFn: () => api.get<{ data: Workspace[] }>('/powerbi/workspaces').then(r => r.data.data),
    enabled: tab === 'workspaces' || tab === 'reports' || tab === 'datasets',
  });

  /* ── Reports ── */
  const reportsQuery = useQuery({
    queryKey: ['pbi-reports', workspaceFilter],
    queryFn: () => {
      const url = workspaceFilter
        ? `/powerbi/workspaces/${workspaceFilter}/reports`
        : '/powerbi/reports';
      return api.get<{ data: Report[] }>(url).then(r => r.data.data);
    },
    enabled: tab === 'reports',
  });

  const importReport = useMutation({
    mutationFn: (r: Report) => api.post(`/powerbi/workspaces/${r.workspaceId}/reports/${r.id}/import`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['pbi-reports'] }),
  });

  const exportReport = useMutation({
    mutationFn: (r: Report) => api.post(`/powerbi/workspaces/${r.workspaceId}/reports/${r.id}/export`),
    onSuccess: () => setExportingReport(null),
  });

  /* ── Datasets ── */
  const datasetsQuery = useQuery({
    queryKey: ['pbi-datasets'],
    queryFn: () => api.get<{ data: Dataset[] }>('/powerbi/datasets').then(r => r.data.data),
    enabled: tab === 'datasets',
  });

  const refreshDataset = useMutation({
    mutationFn: (d: Dataset) => api.post(`/powerbi/workspaces/${d.workspaceId}/datasets/${d.id}/refresh`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['pbi-datasets'] }),
  });

  const refreshHistoryQuery = useQuery({
    queryKey: ['pbi-refresh-history', refreshHistoryDataset?.workspaceId, refreshHistoryDataset?.id],
    queryFn: () =>
      api
        .get<{ data: RefreshEntry[] }>(
          `/powerbi/workspaces/${refreshHistoryDataset!.workspaceId}/datasets/${refreshHistoryDataset!.id}/refreshes`
        )
        .then(r => r.data.data),
    enabled: refreshHistoryDataset !== null,
  });

  /* ── Sync ── */
  const syncHistory = useQuery({
    queryKey: ['pbi-sync-history'],
    queryFn: () => api.get<{ data: SyncHistoryEntry[] }>('/powerbi/sync/history').then(r => r.data.data),
    enabled: tab === 'sync',
  });

  const syncAll = useMutation({
    mutationFn: () => api.post('/powerbi/sync'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pbi-sync-history'] });
      queryClient.invalidateQueries({ queryKey: ['pbi-workspaces'] });
      queryClient.invalidateQueries({ queryKey: ['pbi-reports'] });
      queryClient.invalidateQueries({ queryKey: ['pbi-datasets'] });
    },
  });

  const syncWorkspaces = useMutation({
    mutationFn: () => api.post('/powerbi/sync/workspaces'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pbi-sync-history'] });
      queryClient.invalidateQueries({ queryKey: ['pbi-workspaces'] });
    },
  });

  const syncReports = useMutation({
    mutationFn: () => api.post('/powerbi/sync/reports'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pbi-sync-history'] });
      queryClient.invalidateQueries({ queryKey: ['pbi-reports'] });
    },
  });

  /* ── Column defs ── */

  const workspaceCols: Column<Workspace>[] = [
    { key: 'name', header: 'Name', render: (w) => <span className="font-medium text-gray-900">{w.name}</span> },
    { key: 'type', header: 'Type' },
    { key: 'state', header: 'State', render: (w) => <Badge text={w.state ?? 'Unknown'} variant={statusVariant(w.state)} /> },
    {
      key: 'isOnDedicatedCapacity',
      header: 'Dedicated Capacity',
      render: (w) => (
        <Badge text={w.isOnDedicatedCapacity ? 'Yes' : 'No'} variant={w.isOnDedicatedCapacity ? 'success' : 'info'} />
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-40',
      render: (w) => (
        <button
          onClick={() => { setWorkspaceFilter(w.id); setTab('reports'); }}
          className="text-blue-600 hover:text-blue-800 text-sm font-medium"
        >
          View Reports
        </button>
      ),
    },
  ];

  const reportCols: Column<Report>[] = [
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium text-gray-900">{r.name}</span> },
    { key: 'workspaceName', header: 'Workspace' },
    { key: 'reportType', header: 'Type' },
    { key: 'datasetId', header: 'Dataset', render: (r) => <span className="text-sm text-gray-600">{r.datasetId ?? '-'}</span> },
    { key: 'modifiedDateTime', header: 'Modified', render: (r) => <span className="text-sm">{formatDateTime(r.modifiedDateTime)}</span> },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-60',
      render: (r) => (
        <div className="flex gap-2">
          <button
            onClick={() => importReport.mutate(r)}
            disabled={importReport.isPending}
            className="text-green-600 hover:text-green-800 text-sm font-medium"
          >
            Import
          </button>
          <button
            onClick={() => navigate(`/powerbi/embed/${r.workspaceId}/${r.id}`)}
            className="text-blue-600 hover:text-blue-800 text-sm font-medium"
          >
            Embed
          </button>
          <button
            onClick={() => { setExportingReport(r); exportReport.mutate(r); }}
            disabled={exportReport.isPending}
            className="text-purple-600 hover:text-purple-800 text-sm font-medium"
          >
            Export
          </button>
        </div>
      ),
    },
  ];

  const datasetCols: Column<Dataset>[] = [
    { key: 'name', header: 'Name', render: (d) => <span className="font-medium text-gray-900">{d.name}</span> },
    { key: 'workspaceName', header: 'Workspace' },
    {
      key: 'isRefreshable',
      header: 'Refreshable',
      render: (d) => <Badge text={d.isRefreshable ? 'Yes' : 'No'} variant={d.isRefreshable ? 'success' : 'info'} />,
    },
    { key: 'configuredBy', header: 'Configured By', render: (d) => <span className="text-sm text-gray-600">{d.configuredBy ?? '-'}</span> },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-48',
      render: (d) => (
        <div className="flex gap-2">
          <button
            onClick={() => refreshDataset.mutate(d)}
            disabled={!d.isRefreshable || refreshDataset.isPending}
            className="text-green-600 hover:text-green-800 text-sm font-medium disabled:opacity-40"
          >
            Refresh
          </button>
          <button
            onClick={() => setRefreshHistoryDataset(d)}
            className="text-purple-600 hover:text-purple-800 text-sm font-medium"
          >
            History
          </button>
        </div>
      ),
    },
  ];

  const syncCols: Column<SyncHistoryEntry>[] = [
    { key: 'type', header: 'Type', render: (s) => <span className="font-medium text-gray-900">{s.type}</span> },
    { key: 'status', header: 'Status', render: (s) => <Badge text={s.status} variant={statusVariant(s.status)} /> },
    { key: 'startedAt', header: 'Started', render: (s) => <span className="text-sm">{formatDateTime(s.startedAt)}</span> },
    { key: 'completedAt', header: 'Completed', render: (s) => <span className="text-sm">{formatDateTime(s.completedAt)}</span> },
    { key: 'itemsSynced', header: 'Items Synced', render: (s) => <span className="text-sm">{s.itemsSynced ?? '-'}</span> },
    { key: 'errorMessage', header: 'Error', render: (s) => <span className="text-sm text-red-600">{s.errorMessage ?? '-'}</span> },
  ];

  /* ── Filtered data helpers ── */
  const filteredReports = (reportsQuery.data ?? []).filter(
    (r) => !reportSearch || r.name.toLowerCase().includes(reportSearch.toLowerCase())
  );
  const filteredDatasets = (datasetsQuery.data ?? []).filter(
    (d) => !datasetSearch || d.name.toLowerCase().includes(datasetSearch.toLowerCase())
  );

  /* ── Render ── */
  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Power BI Integration</h2>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-gray-200 mb-6">
        {([
          ['config', 'Configuration'],
          ['workspaces', 'Workspaces'],
          ['reports', 'Reports'],
          ['datasets', 'Datasets'],
          ['sync', 'Sync'],
        ] as [Tab, string][]).map(([key, label]) => (
          <button key={key} onClick={() => setTab(key)} className={tabClasses(tab === key)}>
            {label}
          </button>
        ))}
      </div>

      {/* ── Configuration Tab ── */}
      {tab === 'config' && (
        <div className="bg-white rounded-lg shadow p-6 max-w-2xl">
          <div className="flex items-center justify-between mb-6">
            <h3 className="text-lg font-semibold">Azure AD Credentials</h3>
            <Badge
              text={configQuery.data?.connected ? 'Connected' : 'Disconnected'}
              variant={configQuery.data?.connected ? 'success' : 'error'}
            />
          </div>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Tenant ID</label>
              <input
                type="text"
                value={configForm.tenantId}
                onChange={(e) => setConfigForm({ ...configForm, tenantId: e.target.value })}
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                className={inputCls}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Client ID</label>
              <input
                type="text"
                value={configForm.clientId}
                onChange={(e) => setConfigForm({ ...configForm, clientId: e.target.value })}
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                className={inputCls}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Client Secret</label>
              <input
                type="password"
                value={configForm.clientSecret}
                onChange={(e) => setConfigForm({ ...configForm, clientSecret: e.target.value })}
                placeholder="Enter client secret"
                className={inputCls}
              />
            </div>
            <div className="flex gap-3 pt-2">
              <button
                onClick={() => testConnection.mutate()}
                disabled={testConnection.isPending || !configForm.tenantId || !configForm.clientId}
                className={btnSecondary}
              >
                {testConnection.isPending ? 'Testing...' : 'Test Connection'}
              </button>
              <button
                onClick={() => saveConfig.mutate()}
                disabled={saveConfig.isPending || !configForm.tenantId || !configForm.clientId}
                className={btnPrimary}
              >
                {saveConfig.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
            {testConnection.isSuccess && (
              <p className={`text-sm ${testConnection.data.data.success ? 'text-green-600' : 'text-red-600'}`}>
                {testConnection.data.data.success
                  ? 'Connection successful.'
                  : testConnection.data.data.message ?? 'Connection failed.'}
              </p>
            )}
            {testConnection.isError && <p className="text-sm text-red-600">Connection test failed. Check credentials and try again.</p>}
            {saveConfig.isSuccess && <p className="text-sm text-green-600">Configuration saved successfully.</p>}
            {saveConfig.isError && <p className="text-sm text-red-600">Failed to save configuration.</p>}
          </div>
        </div>
      )}

      {/* ── Workspaces Tab ── */}
      {tab === 'workspaces' && (
        <DataTable
          columns={workspaceCols}
          data={workspacesQuery.data ?? []}
          loading={workspacesQuery.isLoading}
          searchPlaceholder="Search workspaces..."
        />
      )}

      {/* ── Reports Tab ── */}
      {tab === 'reports' && (
        <div>
          {/* Workspace filter */}
          <div className="mb-4 flex items-center gap-3">
            <label className="text-sm font-medium text-gray-700">Workspace:</label>
            <select
              value={workspaceFilter}
              onChange={(e) => setWorkspaceFilter(e.target.value)}
              className="border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="">All Workspaces</option>
              {(workspacesQuery.data ?? []).map((w) => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
          </div>
          <DataTable
            columns={reportCols}
            data={filteredReports}
            loading={reportsQuery.isLoading}
            searchValue={reportSearch}
            onSearch={setReportSearch}
            searchPlaceholder="Search reports..."
          />
        </div>
      )}

      {/* ── Datasets Tab ── */}
      {tab === 'datasets' && (
        <DataTable
          columns={datasetCols}
          data={filteredDatasets}
          loading={datasetsQuery.isLoading}
          searchValue={datasetSearch}
          onSearch={setDatasetSearch}
          searchPlaceholder="Search datasets..."
        />
      )}

      {/* ── Sync Tab ── */}
      {tab === 'sync' && (
        <div>
          <div className="flex gap-3 mb-6">
            <button
              onClick={() => syncAll.mutate()}
              disabled={syncAll.isPending}
              className={btnPrimary}
            >
              {syncAll.isPending ? 'Syncing...' : 'Sync All'}
            </button>
            <button
              onClick={() => syncWorkspaces.mutate()}
              disabled={syncWorkspaces.isPending}
              className={btnSecondary}
            >
              {syncWorkspaces.isPending ? 'Syncing...' : 'Sync Workspaces'}
            </button>
            <button
              onClick={() => syncReports.mutate()}
              disabled={syncReports.isPending}
              className={btnSecondary}
            >
              {syncReports.isPending ? 'Syncing...' : 'Sync Reports'}
            </button>
          </div>
          {syncAll.isSuccess && <p className="text-sm text-green-600 mb-4">Full sync completed.</p>}
          {syncAll.isError && <p className="text-sm text-red-600 mb-4">Sync failed. Check configuration and try again.</p>}

          {/* Last sync */}
          {syncHistory.data && syncHistory.data.length > 0 && (
            <p className="text-sm text-gray-500 mb-4">
              Last sync: {formatDateTime(syncHistory.data[0].completedAt ?? syncHistory.data[0].startedAt)}
            </p>
          )}

          <DataTable
            columns={syncCols}
            data={syncHistory.data ?? []}
            loading={syncHistory.isLoading}
          />
        </div>
      )}

      {/* ── Refresh History Modal ── */}
      <Modal
        isOpen={refreshHistoryDataset !== null}
        onClose={() => setRefreshHistoryDataset(null)}
        title={`Refresh History — ${refreshHistoryDataset?.name ?? ''}`}
        wide
      >
        {refreshHistoryQuery.isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="h-4 bg-gray-200 rounded animate-pulse" />
            ))}
          </div>
        ) : refreshHistoryQuery.isError ? (
          <p className="text-sm text-gray-500">Failed to load refresh history.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Start Time</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">End Time</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Request ID</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {(refreshHistoryQuery.data ?? []).length === 0 ? (
                  <tr>
                    <td colSpan={4} className="px-4 py-6 text-center text-gray-400">No refresh history found.</td>
                  </tr>
                ) : (
                  (refreshHistoryQuery.data ?? []).map((entry) => (
                    <tr key={entry.id}>
                      <td className="px-4 py-2"><Badge text={entry.status} variant={statusVariant(entry.status)} /></td>
                      <td className="px-4 py-2">{formatDateTime(entry.startTime)}</td>
                      <td className="px-4 py-2">{formatDateTime(entry.endTime)}</td>
                      <td className="px-4 py-2 text-gray-600">{entry.requestId ?? '-'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </Modal>

      {/* ── Export Modal ── */}
      <Modal
        isOpen={exportingReport !== null && exportReport.isPending}
        onClose={() => setExportingReport(null)}
        title={`Exporting — ${exportingReport?.name ?? ''}`}
      >
        <div className="flex items-center gap-3">
          <div className="h-5 w-5 border-2 border-blue-600 border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-gray-600">Exporting report, please wait...</p>
        </div>
      </Modal>
    </div>
  );
}
