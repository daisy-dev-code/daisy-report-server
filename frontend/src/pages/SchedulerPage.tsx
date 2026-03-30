import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

interface Job {
  id: number;
  name: string;
  description?: string;
  status: string;
  schedule: string;
  nextFireTime?: string;
  lastFireTime?: string;
  reportId?: number;
  outputFormat?: string;
}

interface PaginatedResponse {
  items: Job[];
  totalItems: number;
  totalPages: number;
  page: number;
  pageSize: number;
}

interface Execution {
  id: number;
  status: string;
  startTime: string;
  endTime?: string;
  message?: string;
}

interface JobForm {
  name: string;
  description: string;
  schedule: string;
  reportId: string;
  outputFormat: string;
}

const emptyForm: JobForm = { name: '', description: '', schedule: '', reportId: '', outputFormat: 'PDF' };

function statusBadgeVariant(status: string): 'info' | 'warning' | 'success' | 'error' {
  switch (status?.toUpperCase()) {
    case 'WAITING': case 'SCHEDULED': return 'info';
    case 'EXECUTING': case 'RUNNING': return 'warning';
    case 'COMPLETED': case 'COMPLETE': return 'success';
    case 'FAILED': case 'ERROR': return 'error';
    default: return 'info';
  }
}

export default function SchedulerPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<JobForm>(emptyForm);
  const [historyJobId, setHistoryJobId] = useState<number | null>(null);

  const pageSize = 20;

  const { data, isLoading } = useQuery({
    queryKey: ['scheduler-jobs', page, search],
    queryFn: () =>
      api.get<PaginatedResponse>('/scheduler/jobs', { params: { page, pageSize, search: search || undefined } }).then(r => r.data),
  });

  const historyQuery = useQuery({
    queryKey: ['job-history', historyJobId],
    queryFn: () =>
      api.get<{ items: Execution[] }>(`/scheduler/jobs/${historyJobId}/executions`).then(r => r.data.items ?? r.data),
    enabled: historyJobId !== null,
  });

  const saveMutation = useMutation({
    mutationFn: (payload: JobForm) => {
      const body = { ...payload, reportId: payload.reportId ? Number(payload.reportId) : undefined };
      return editingId
        ? api.put(`/scheduler/jobs/${editingId}`, body)
        : api.post('/scheduler/jobs', body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scheduler-jobs'] });
      closeModal();
    },
  });

  const executeMutation = useMutation({
    mutationFn: (id: number) => api.post(`/scheduler/jobs/${id}/execute`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scheduler-jobs'] });
    },
  });

  function openCreate() {
    setEditingId(null);
    setForm(emptyForm);
    setModalOpen(true);
  }

  function openEdit(job: Job) {
    setEditingId(job.id);
    setForm({
      name: job.name,
      description: job.description ?? '',
      schedule: job.schedule,
      reportId: job.reportId?.toString() ?? '',
      outputFormat: job.outputFormat ?? 'PDF',
    });
    setModalOpen(true);
  }

  function closeModal() {
    setModalOpen(false);
    setEditingId(null);
    setForm(emptyForm);
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    saveMutation.mutate(form);
  }

  function formatDateTime(val?: string) {
    if (!val) return '-';
    return new Date(val).toLocaleString();
  }

  const columns: Column<Job>[] = [
    { key: 'name', header: 'Name', render: (j) => <span className="font-medium text-gray-900">{j.name}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (j) => <Badge text={j.status} variant={statusBadgeVariant(j.status)} />,
    },
    { key: 'schedule', header: 'Schedule', render: (j) => <code className="text-xs bg-gray-100 px-2 py-1 rounded">{j.schedule}</code> },
    { key: 'nextFireTime', header: 'Next Fire', render: (j) => <span className="text-sm">{formatDateTime(j.nextFireTime)}</span> },
    { key: 'lastFireTime', header: 'Last Fire', render: (j) => <span className="text-sm">{formatDateTime(j.lastFireTime)}</span> },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-56',
      render: (j) => (
        <div className="flex gap-2">
          <button
            onClick={() => executeMutation.mutate(j.id)}
            disabled={executeMutation.isPending}
            className="text-green-600 hover:text-green-800 text-sm font-medium"
          >
            Run Now
          </button>
          <button onClick={() => setHistoryJobId(j.id)} className="text-purple-600 hover:text-purple-800 text-sm font-medium">
            History
          </button>
          <button onClick={() => openEdit(j)} className="text-blue-600 hover:text-blue-800 text-sm font-medium">
            Edit
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Scheduler</h2>
      <DataTable
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        pagination={data ? { page: data.page, pageSize: data.pageSize, totalItems: data.totalItems, totalPages: data.totalPages } : undefined}
        onPageChange={setPage}
        searchValue={search}
        onSearch={(v) => { setSearch(v); setPage(1); }}
        searchPlaceholder="Search jobs..."
        actions={
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700">
            New Job
          </button>
        }
      />

      {/* Create/Edit Modal */}
      <Modal isOpen={modalOpen} onClose={closeModal} title={editingId ? 'Edit Job' : 'New Job'}>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              rows={2}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Cron Schedule</label>
            <input
              type="text"
              value={form.schedule}
              onChange={(e) => setForm({ ...form, schedule: e.target.value })}
              placeholder="0 0 8 * * ?"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Report ID</label>
              <input
                type="number"
                value={form.reportId}
                onChange={(e) => setForm({ ...form, reportId: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Output Format</label>
              <select
                value={form.outputFormat}
                onChange={(e) => setForm({ ...form, outputFormat: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="PDF">PDF</option>
                <option value="XLSX">Excel</option>
                <option value="CSV">CSV</option>
                <option value="HTML">HTML</option>
              </select>
            </div>
          </div>
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={closeModal} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50">
              Cancel
            </button>
            <button type="submit" disabled={saveMutation.isPending} className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
              {saveMutation.isPending ? 'Saving...' : 'Save'}
            </button>
          </div>
          {saveMutation.isError && (
            <p className="text-red-600 text-sm">Failed to save job. Please try again.</p>
          )}
        </form>
      </Modal>

      {/* Execution History Modal */}
      <Modal isOpen={historyJobId !== null} onClose={() => setHistoryJobId(null)} title="Execution History" wide>
        {historyQuery.isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="h-4 bg-gray-200 rounded animate-pulse" />
            ))}
          </div>
        ) : historyQuery.isError ? (
          <p className="text-gray-500 text-sm">Failed to load execution history.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Start Time</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">End Time</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Message</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {(Array.isArray(historyQuery.data) ? historyQuery.data : []).length === 0 ? (
                  <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">No executions found.</td></tr>
                ) : (
                  (Array.isArray(historyQuery.data) ? historyQuery.data : []).map((exec) => (
                    <tr key={exec.id}>
                      <td className="px-4 py-2"><Badge text={exec.status} variant={statusBadgeVariant(exec.status)} /></td>
                      <td className="px-4 py-2">{formatDateTime(exec.startTime)}</td>
                      <td className="px-4 py-2">{formatDateTime(exec.endTime)}</td>
                      <td className="px-4 py-2 text-gray-600">{exec.message ?? '-'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </Modal>
    </div>
  );
}
