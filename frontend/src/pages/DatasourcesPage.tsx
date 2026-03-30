import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

interface Datasource {
  id: number;
  name: string;
  description?: string;
  type: string;
  jdbcUrl?: string;
  username?: string;
  created: string;
}

interface PaginatedResponse {
  items: Datasource[];
  totalItems: number;
  totalPages: number;
  page: number;
  pageSize: number;
}

interface DatasourceForm {
  name: string;
  description: string;
  type: string;
  jdbcUrl: string;
  username: string;
  password: string;
}

interface TestResult {
  id: number;
  status: 'idle' | 'testing' | 'success' | 'error';
  message?: string;
}

const emptyForm: DatasourceForm = { name: '', description: '', type: 'MYSQL', jdbcUrl: '', username: '', password: '' };

export default function DatasourcesPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<DatasourceForm>(emptyForm);
  const [deleteId, setDeleteId] = useState<number | null>(null);
  const [testResults, setTestResults] = useState<Record<number, TestResult>>({});

  const pageSize = 20;

  const { data, isLoading } = useQuery({
    queryKey: ['datasources', page, search],
    queryFn: () =>
      api.get<PaginatedResponse>('/datasources', { params: { page, pageSize, search: search || undefined } }).then(r => r.data),
  });

  const saveMutation = useMutation({
    mutationFn: (payload: DatasourceForm) => {
      const body: Record<string, unknown> = { ...payload };
      if (editingId && !payload.password) {
        delete body.password;
      }
      return editingId
        ? api.put(`/datasources/${editingId}`, body)
        : api.post('/datasources', body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['datasources'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => api.delete(`/datasources/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['datasources'] });
      setDeleteId(null);
    },
  });

  async function testConnection(id: number) {
    setTestResults((prev) => ({ ...prev, [id]: { id, status: 'testing' } }));
    try {
      const res = await api.post<{ success?: boolean; message?: string }>(`/datasources/${id}/test`);
      const success = res.data.success !== false;
      setTestResults((prev) => ({
        ...prev,
        [id]: { id, status: success ? 'success' : 'error', message: res.data.message ?? (success ? 'Connection successful' : 'Connection failed') },
      }));
    } catch {
      setTestResults((prev) => ({
        ...prev,
        [id]: { id, status: 'error', message: 'Connection test failed' },
      }));
    }
  }

  function openCreate() {
    setEditingId(null);
    setForm(emptyForm);
    setModalOpen(true);
  }

  function openEdit(ds: Datasource) {
    setEditingId(ds.id);
    setForm({
      name: ds.name,
      description: ds.description ?? '',
      type: ds.type,
      jdbcUrl: ds.jdbcUrl ?? '',
      username: ds.username ?? '',
      password: '',
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

  const columns: Column<Datasource>[] = [
    { key: 'name', header: 'Name', render: (d) => <span className="font-medium text-gray-900">{d.name}</span> },
    { key: 'type', header: 'Type' },
    { key: 'created', header: 'Created', render: (d) => new Date(d.created).toLocaleDateString() },
    {
      key: 'test',
      header: 'Connection',
      render: (d) => {
        const result = testResults[d.id];
        return (
          <div className="flex items-center gap-2">
            <button
              onClick={() => testConnection(d.id)}
              disabled={result?.status === 'testing'}
              className="text-sm text-blue-600 hover:text-blue-800 font-medium disabled:opacity-50"
            >
              {result?.status === 'testing' ? 'Testing...' : 'Test'}
            </button>
            {result?.status === 'success' && <Badge text="OK" variant="success" />}
            {result?.status === 'error' && (
              <span className="text-xs text-red-600" title={result.message}>{result.message}</span>
            )}
          </div>
        );
      },
    },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-40',
      render: (d) => (
        <div className="flex gap-2">
          <button onClick={() => openEdit(d)} className="text-blue-600 hover:text-blue-800 text-sm font-medium">
            Edit
          </button>
          <button onClick={() => setDeleteId(d.id)} className="text-red-600 hover:text-red-800 text-sm font-medium">
            Delete
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Datasources</h2>
      <DataTable
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        pagination={data ? { page: data.page, pageSize: data.pageSize, totalItems: data.totalItems, totalPages: data.totalPages } : undefined}
        onPageChange={setPage}
        searchValue={search}
        onSearch={(v) => { setSearch(v); setPage(1); }}
        searchPlaceholder="Search datasources..."
        actions={
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700">
            New Datasource
          </button>
        }
      />

      {/* Create/Edit Modal */}
      <Modal isOpen={modalOpen} onClose={closeModal} title={editingId ? 'Edit Datasource' : 'New Datasource'} wide>
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
            <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
            <select
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="MYSQL">MySQL</option>
              <option value="POSTGRES">PostgreSQL</option>
              <option value="MSSQL">MS SQL Server</option>
              <option value="ORACLE">Oracle</option>
              <option value="H2">H2</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">JDBC URL</label>
            <input
              type="text"
              value={form.jdbcUrl}
              onChange={(e) => setForm({ ...form, jdbcUrl: e.target.value })}
              placeholder="jdbc:mysql://localhost:3306/mydb"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Username</label>
              <input
                type="text"
                value={form.username}
                onChange={(e) => setForm({ ...form, username: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Password {editingId && <span className="text-gray-400 font-normal">(leave blank to keep current)</span>}
              </label>
              <input
                type="password"
                value={form.password}
                onChange={(e) => setForm({ ...form, password: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
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
            <p className="text-red-600 text-sm">Failed to save datasource. Please try again.</p>
          )}
        </form>
      </Modal>

      {/* Delete Confirmation */}
      <Modal isOpen={deleteId !== null} onClose={() => setDeleteId(null)} title="Delete Datasource">
        <p className="text-sm text-gray-600 mb-6">Are you sure you want to delete this datasource? This action cannot be undone.</p>
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
