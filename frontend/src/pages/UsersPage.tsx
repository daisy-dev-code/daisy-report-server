import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

interface User {
  id: number;
  username: string;
  firstname: string;
  lastname: string;
  email: string;
  enabled: boolean;
}

interface PaginatedResponse {
  items: User[];
  totalItems: number;
  totalPages: number;
  page: number;
  pageSize: number;
}

interface UserForm {
  username: string;
  firstname: string;
  lastname: string;
  email: string;
  password: string;
  enabled: boolean;
}

const emptyForm: UserForm = { username: '', firstname: '', lastname: '', email: '', password: '', enabled: true };

export default function UsersPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<UserForm>(emptyForm);
  const [deleteId, setDeleteId] = useState<number | null>(null);

  const pageSize = 20;

  const { data, isLoading } = useQuery({
    queryKey: ['users', page, search],
    queryFn: () =>
      api.get<PaginatedResponse>('/users', { params: { page, pageSize, search: search || undefined } }).then(r => r.data),
  });

  const saveMutation = useMutation({
    mutationFn: (payload: UserForm) => {
      const body: Record<string, unknown> = { ...payload };
      if (editingId && !payload.password) {
        delete body.password;
      }
      return editingId
        ? api.put(`/users/${editingId}`, body)
        : api.post('/users', body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => api.delete(`/users/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      setDeleteId(null);
    },
  });

  const toggleMutation = useMutation({
    mutationFn: (user: User) => api.put(`/users/${user.id}`, { ...user, enabled: !user.enabled }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
    },
  });

  function openCreate() {
    setEditingId(null);
    setForm(emptyForm);
    setModalOpen(true);
  }

  function openEdit(user: User) {
    setEditingId(user.id);
    setForm({
      username: user.username,
      firstname: user.firstname,
      lastname: user.lastname,
      email: user.email,
      password: '',
      enabled: user.enabled,
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

  const columns: Column<User>[] = [
    { key: 'username', header: 'Username', render: (u) => <span className="font-medium text-gray-900">{u.username}</span> },
    { key: 'name', header: 'Name', render: (u) => `${u.firstname ?? ''} ${u.lastname ?? ''}`.trim() || '-' },
    { key: 'email', header: 'Email' },
    {
      key: 'enabled',
      header: 'Status',
      render: (u) => (
        <button onClick={() => toggleMutation.mutate(u)} title={u.enabled ? 'Click to disable' : 'Click to enable'}>
          <Badge text={u.enabled ? 'Enabled' : 'Disabled'} variant={u.enabled ? 'success' : 'error'} />
        </button>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      className: 'w-40',
      render: (u) => (
        <div className="flex gap-2">
          <button onClick={() => openEdit(u)} className="text-blue-600 hover:text-blue-800 text-sm font-medium">
            Edit
          </button>
          <button onClick={() => setDeleteId(u.id)} className="text-red-600 hover:text-red-800 text-sm font-medium">
            Delete
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Users</h2>
      <DataTable
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        pagination={data ? { page: data.page, pageSize: data.pageSize, totalItems: data.totalItems, totalPages: data.totalPages } : undefined}
        onPageChange={setPage}
        searchValue={search}
        onSearch={(v) => { setSearch(v); setPage(1); }}
        searchPlaceholder="Search users..."
        actions={
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700">
            New User
          </button>
        }
      />

      {/* Create/Edit Modal */}
      <Modal isOpen={modalOpen} onClose={closeModal} title={editingId ? 'Edit User' : 'New User'}>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Username</label>
            <input
              type="text"
              value={form.username}
              onChange={(e) => setForm({ ...form, username: e.target.value })}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
              disabled={editingId !== null}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">First Name</label>
              <input
                type="text"
                value={form.firstname}
                onChange={(e) => setForm({ ...form, firstname: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Last Name</label>
              <input
                type="text"
                value={form.lastname}
                onChange={(e) => setForm({ ...form, lastname: e.target.value })}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
            <input
              type="email"
              value={form.email}
              onChange={(e) => setForm({ ...form, email: e.target.value })}
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
              required={!editingId}
            />
          </div>
          <div className="flex items-center gap-2">
            <input
              type="checkbox"
              id="enabled"
              checked={form.enabled}
              onChange={(e) => setForm({ ...form, enabled: e.target.checked })}
              className="rounded border-gray-300"
            />
            <label htmlFor="enabled" className="text-sm text-gray-700">Enabled</label>
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
            <p className="text-red-600 text-sm">Failed to save user. Please try again.</p>
          )}
        </form>
      </Modal>

      {/* Delete Confirmation */}
      <Modal isOpen={deleteId !== null} onClose={() => setDeleteId(null)} title="Delete User">
        <p className="text-sm text-gray-600 mb-6">Are you sure you want to delete this user? This action cannot be undone.</p>
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
