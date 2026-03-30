import { type ReactNode } from 'react';

export interface Column<T> {
  key: string;
  header: string;
  render?: (row: T) => ReactNode;
  className?: string;
}

interface Pagination {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

interface DataTableProps<T> {
  columns: Column<T>[];
  data: T[];
  loading?: boolean;
  pagination?: Pagination;
  onPageChange?: (page: number) => void;
  searchValue?: string;
  onSearch?: (value: string) => void;
  searchPlaceholder?: string;
  actions?: ReactNode;
}

function getNestedValue(obj: unknown, path: string): unknown {
  return path.split('.').reduce((acc: unknown, key) => {
    if (acc && typeof acc === 'object' && key in (acc as Record<string, unknown>)) {
      return (acc as Record<string, unknown>)[key];
    }
    return undefined;
  }, obj);
}

export default function DataTable<T>({
  columns,
  data,
  loading,
  pagination,
  onPageChange,
  searchValue,
  onSearch,
  searchPlaceholder = 'Search...',
  actions,
}: DataTableProps<T>) {
  return (
    <div>
      {(onSearch || actions) && (
        <div className="flex items-center justify-between mb-4 gap-4">
          {onSearch ? (
            <input
              type="text"
              value={searchValue ?? ''}
              onChange={(e) => onSearch(e.target.value)}
              placeholder={searchPlaceholder}
              className="border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 w-72"
            />
          ) : (
            <div />
          )}
          {actions && <div className="flex gap-2">{actions}</div>}
        </div>
      )}

      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {columns.map((col) => (
                  <th
                    key={col.key}
                    className={`px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider ${col.className ?? ''}`}
                  >
                    {col.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {loading ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i}>
                    {columns.map((col) => (
                      <td key={col.key} className="px-6 py-4">
                        <div className="h-4 bg-gray-200 rounded animate-pulse" />
                      </td>
                    ))}
                  </tr>
                ))
              ) : data.length === 0 ? (
                <tr>
                  <td
                    colSpan={columns.length}
                    className="px-6 py-12 text-center text-gray-500"
                  >
                    No data found.
                  </td>
                </tr>
              ) : (
                data.map((row, idx) => (
                  <tr key={idx} className="hover:bg-gray-50">
                    {columns.map((col) => (
                      <td key={col.key} className={`px-6 py-4 text-sm ${col.className ?? ''}`}>
                        {col.render
                          ? col.render(row)
                          : String(getNestedValue(row, col.key) ?? '')}
                      </td>
                    ))}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {pagination && pagination.totalPages > 1 && (
          <div className="flex items-center justify-between px-6 py-3 border-t border-gray-200 bg-gray-50">
            <p className="text-sm text-gray-500">
              Showing {(pagination.page - 1) * pagination.pageSize + 1} to{' '}
              {Math.min(pagination.page * pagination.pageSize, pagination.totalItems)} of{' '}
              {pagination.totalItems}
            </p>
            <div className="flex gap-2">
              <button
                onClick={() => onPageChange?.(pagination.page - 1)}
                disabled={pagination.page <= 1}
                className="px-3 py-1 text-sm border border-gray-300 rounded-lg disabled:opacity-50 hover:bg-gray-100"
              >
                Previous
              </button>
              <span className="px-3 py-1 text-sm text-gray-600">
                Page {pagination.page} of {pagination.totalPages}
              </span>
              <button
                onClick={() => onPageChange?.(pagination.page + 1)}
                disabled={pagination.page >= pagination.totalPages}
                className="px-3 py-1 text-sm border border-gray-300 rounded-lg disabled:opacity-50 hover:bg-gray-100"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
