import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';

interface Report {
  id: number;
  name: string;
  engineType: string;
  folder?: string;
  description?: string;
  created: string;
}

interface PaginatedResponse {
  data: Report[];
  pagination: {
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
  };
}

interface ReportForm {
  name: string;
  engineType: string;
  folder: string;
  description: string;
}

const emptyForm: ReportForm = { name: '', engineType: 'BIRT', folder: '', description: '' };

export default function ReportsPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<ReportForm>(emptyForm);
  const [deleteId, setDeleteId] = useState<number | null>(null);

  const pageSize = 20;

  const { data, isLoading } = useQuery({
    queryKey: ['reports', page, search],
    queryFn: () =>
      api.get<PaginatedResponse>('/reports', { params: { page, pageSize, search: search || undefined } }).then(r => r.data),
  });

  const saveMutation = useMutation({
    mutationFn: (payload: ReportForm) =>
      editingId
        ? api.put(`/reports/${editingId}`, payload)
        : api.post('/reports', payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => api.delete(`/reports/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      setDeleteId(null);
    },
  });

  function openCreate() {
    setEditingId(null);
    setForm(emptyForm);
    setModalOpen(true);
  }

  function openEdit(report: Report) {
    setEditingId(report.id);
    setForm({
      name: report.name,
      engineType: report.engineType,
      folder: report.folder ?? '',
      description: report.description ?? '',
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

  const columns: Column<Report>[] = [
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium text-gray-900">{r.name}</span> },
    { key: 'engineType', header: 'Engine Type' },
    { key: 'folder', header: 'Folder' },
    { key: 'created', header: 'Created', render: (r) => new Date(r.created).toLocaleDateString() },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-40',
      render: (r) => (
        <div className="flex gap-2">
          <button onClick={() => openEdit(r)} className="text-blue-600 hover:text-blue-800 text-sm font-medium">
            Edit
          </button>
          <button onClick={() => setDeleteId(r.id)} className="text-red-600 hover:text-red-800 text-sm font-medium">
            Delete
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Reports</h2>
      <DataTable
        columns={columns}
        data={data?.data ?? []}
        loading={isLoading}
        pagination={data ? { page: data.pagination.page, pageSize: data.pagination.pageSize, totalItems: data.pagination.total, totalPages: data.pagination.totalPages } : undefined}
        onPageChange={setPage}
        searchValue={search}
        onSearch={(v) => { setSearch(v); setPage(1); }}
        searchPlaceholder="Search reports..."
        actions={
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700">
            New Report
          </button>
        }
      />

      {/* Create/Edit Modal */}
      <Modal isOpen={modalOpen} onClose={closeModal} title={editingId ? 'Edit Report' : 'New Report'}>
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
            <label className="block text-sm font-medium text-gray-700 mb-1">Engine Type</label>
            <select
              value={form.engineType}
              onChange={(e) => setForm({ ...form, engineType: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="BIRT">BIRT</option>
              <option value="JASPER">Jasper</option>
              <option value="SQL">SQL</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Folder</label>
            <input
              type="text"
              value={form.folder}
              onChange={(e) => setForm({ ...form, folder: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              rows={3}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
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
            <p className="text-red-600 text-sm">Failed to save report. Please try again.</p>
          )}
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal isOpen={deleteId !== null} onClose={() => setDeleteId(null)} title="Delete Report">
        <p className="text-sm text-gray-600 mb-6">Are you sure you want to delete this report? This action cannot be undone.</p>
        <div className="flex justify-end gap-3">
          <button onClick={() => setDeleteId(null)} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50">
            Cancel
          </button>
          <button
            onClick={() => deleteId && deleteMutation.mutate(deleteId)}
            disabled={deleteMutation.isPending}
            className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </button>
        </div>
      </Modal>
    </div>
  );
}
