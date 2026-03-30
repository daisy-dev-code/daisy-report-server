import { Outlet, NavLink } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import api from '../api/client';

const navItems = [
  { path: '/', label: 'Dashboard' },
  { path: '/reports', label: 'Reports' },
  { path: '/datasources', label: 'Datasources' },
  { path: '/scheduler', label: 'Scheduler' },
  { path: '/powerbi', label: 'Power BI' },
  { path: '/users', label: 'Users' },
  { path: '/settings', label: 'Settings' },
];

export default function Layout() {
  const { user, logout } = useAuthStore();

  const handleLogout = async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // Ignore errors — clear local state regardless
    }
    logout();
  };

  return (
    <div className="flex h-screen bg-gray-100">
      <aside className="w-64 bg-gray-900 text-white flex flex-col">
        <div className="p-4 border-b border-gray-700">
          <h1 className="text-xl font-bold">DaisyReport</h1>
          <p className="text-xs text-gray-400">BI Platform</p>
        </div>
        <nav className="flex-1 p-2">
          {navItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 rounded-lg mb-1 text-sm transition-colors ${
                  isActive
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                }`
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="p-4 border-t border-gray-700">
          <p className="text-sm text-gray-300">{user?.username ?? 'User'}</p>
          <button
            onClick={handleLogout}
            className="text-xs text-gray-400 hover:text-white mt-1"
          >
            Sign out
          </button>
        </div>
      </aside>
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
