import { useState, useCallback, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../api/client';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

// ── Types ────────────────────────────────────────────────────────────────────

interface Connection {
  id: number;
  name: string;
  type: string;
}

interface TableInfo {
  schema: string;
  name: string;
}

interface ColumnInfo {
  name: string;
  type: string;
  nullable: boolean;
  primaryKey: boolean;
}

interface QueryResult {
  columns: string[];
  rows: Record<string, unknown>[];
  rowCount: number;
  executionTimeMs: number;
}

interface SavedQuery {
  id: number;
  name: string;
  sql: string;
  datasourceId: number;
  created: string;
}

interface WhereClause {
  column: string;
  operator: string;
  value: string;
}

interface OrderByClause {
  column: string;
  direction: 'ASC' | 'DESC';
}

interface SqlParam {
  name: string;
  value: string;
}

type ActiveTab = 'visual' | 'sql' | 'results';

// ── Constants ────────────────────────────────────────────────────────────────

const OPERATORS = ['=', '!=', '<', '>', '<=', '>=', 'LIKE', 'NOT LIKE', 'IN', 'NOT IN', 'IS NULL', 'IS NOT NULL'];

// ── Component ────────────────────────────────────────────────────────────────

export default function QueryBuilderPage() {
  const queryClient = useQueryClient();

  // Connection & table state
  const [selectedDatasourceId, setSelectedDatasourceId] = useState<number | null>(null);
  const [selectedTable, setSelectedTable] = useState<TableInfo | null>(null);
  const [expandedSchemas, setExpandedSchemas] = useState<Set<string>>(new Set());

  // Query builder state
  const [activeTab, setActiveTab] = useState<ActiveTab>('visual');
  const [selectedColumns, setSelectedColumns] = useState<Set<string>>(new Set());
  const [whereClauses, setWhereClauses] = useState<WhereClause[]>([]);
  const [orderByClauses, setOrderByClauses] = useState<OrderByClause[]>([]);
  const [limit, setLimit] = useState(1000);
  const [sql, setSql] = useState('');
  const [params, setParams] = useState<SqlParam[]>([]);

  // Results state
  const [queryResult, setQueryResult] = useState<QueryResult | null>(null);
  const [resultPage, setResultPage] = useState(1);
  const resultPageSize = 50;

  // Save modal
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const [saveQueryName, setSaveQueryName] = useState('');

  // Sidebar toggle
  const [showSavedQueries, setShowSavedQueries] = useState(false);

  // Status
  const [statusMessage, setStatusMessage] = useState('');

  // Ref for tracking execution
  const executionStartRef = useRef<number>(0);

  // ── API Queries ──────────────────────────────────────────────────────────

  const { data: connectionsData } = useQuery({
    queryKey: ['spreadsheet-connections'],
    queryFn: () =>
      api.get<{ data: Connection[] }>('/spreadsheet/connections').then(r => r.data),
  });
  const connections = connectionsData?.data ?? [];

  const { data: tablesData, isLoading: tablesLoading } = useQuery({
    queryKey: ['metadata-tables', selectedDatasourceId],
    queryFn: () =>
      api.get<{ data: TableInfo[] }>(`/metadata/${selectedDatasourceId}/tables`).then(r => r.data),
    enabled: selectedDatasourceId !== null,
  });
  const tables = tablesData?.data ?? [];

  const { data: columnsData, isLoading: columnsLoading } = useQuery({
    queryKey: ['metadata-columns', selectedDatasourceId, selectedTable?.schema, selectedTable?.name],
    queryFn: () =>
      api.get<{ data: ColumnInfo[] }>(
        `/metadata/${selectedDatasourceId}/tables/${selectedTable!.schema}/${selectedTable!.name}/columns`
      ).then(r => r.data),
    enabled: selectedDatasourceId !== null && selectedTable !== null,
  });
  const columns = columnsData?.data ?? [];

  const { data: savedQueriesData } = useQuery({
    queryKey: ['spreadsheet-queries'],
    queryFn: () =>
      api.get<{ data: SavedQuery[] }>('/spreadsheet/queries').then(r => r.data),
  });
  const savedQueries = savedQueriesData?.data ?? [];

  // ── Mutations ────────────────────────────────────────────────────────────

  const executeMutation = useMutation({
    mutationFn: (payload: { datasourceId: number; sql: string; parameters?: Record<string, string> }) =>
      api.post<QueryResult>('/spreadsheet/query', payload).then(r => r.data),
    onSuccess: (data) => {
      setQueryResult(data);
      setResultPage(1);
      setActiveTab('results');
      setStatusMessage(`Rows: ${data.rowCount} | Time: ${data.executionTimeMs}ms`);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Query execution failed';
      setStatusMessage(`Error: ${msg}`);
    },
  });

  const saveQueryMutation = useMutation({
    mutationFn: (payload: { name: string; sql: string; datasourceId: number }) =>
      api.post('/spreadsheet/queries', payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['spreadsheet-queries'] });
      setSaveModalOpen(false);
      setSaveQueryName('');
      setStatusMessage('Query saved successfully');
    },
  });

  const deleteQueryMutation = useMutation({
    mutationFn: (id: number) => api.delete(`/spreadsheet/queries/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['spreadsheet-queries'] });
    },
  });

  // ── Handlers ─────────────────────────────────────────────────────────────

  function handleDatasourceChange(id: number) {
    setSelectedDatasourceId(id);
    setSelectedTable(null);
    setSelectedColumns(new Set());
    setWhereClauses([]);
    setOrderByClauses([]);
    setQueryResult(null);
    setSql('');
    const ds = connections.find(c => c.id === id);
    setStatusMessage(ds ? `Connected to ${ds.name}` : '');
  }

  function toggleSchema(schema: string) {
    setExpandedSchemas(prev => {
      const next = new Set(prev);
      if (next.has(schema)) next.delete(schema);
      else next.add(schema);
      return next;
    });
  }

  function selectTable(table: TableInfo) {
    setSelectedTable(table);
    setSelectedColumns(new Set());
  }

  function toggleColumn(colName: string) {
    setSelectedColumns(prev => {
      const next = new Set(prev);
      if (next.has(colName)) next.delete(colName);
      else next.add(colName);
      return next;
    });
  }

  function addWhereClause() {
    setWhereClauses(prev => [...prev, { column: columns[0]?.name ?? '', operator: '=', value: '' }]);
  }

  function updateWhereClause(index: number, field: keyof WhereClause, value: string) {
    setWhereClauses(prev => prev.map((w, i) => i === index ? { ...w, [field]: value } : w));
  }

  function removeWhereClause(index: number) {
    setWhereClauses(prev => prev.filter((_, i) => i !== index));
  }

  function addOrderBy() {
    setOrderByClauses(prev => [...prev, { column: columns[0]?.name ?? '', direction: 'ASC' }]);
  }

  function updateOrderBy(index: number, field: keyof OrderByClause, value: string) {
    setOrderByClauses(prev => prev.map((o, i) => i === index ? { ...o, [field]: value } : o));
  }

  function removeOrderBy(index: number) {
    setOrderByClauses(prev => prev.filter((_, i) => i !== index));
  }

  function addParam() {
    setParams(prev => [...prev, { name: '', value: '' }]);
  }

  function updateParam(index: number, field: keyof SqlParam, value: string) {
    setParams(prev => prev.map((p, i) => i === index ? { ...p, [field]: value } : p));
  }

  function removeParam(index: number) {
    setParams(prev => prev.filter((_, i) => i !== index));
  }

  const generateSql = useCallback(() => {
    if (!selectedTable) return '';
    const cols = selectedColumns.size > 0 ? Array.from(selectedColumns).join(', ') : '*';
    let query = `SELECT ${cols}\nFROM ${selectedTable.schema}.${selectedTable.name}`;

    const validWhere = whereClauses.filter(w => w.column);
    if (validWhere.length > 0) {
      const conditions = validWhere.map(w => {
        if (w.operator === 'IS NULL' || w.operator === 'IS NOT NULL') {
          return `${w.column} ${w.operator}`;
        }
        return `${w.column} ${w.operator} '${w.value}'`;
      });
      query += `\nWHERE ${conditions.join('\n  AND ')}`;
    }

    if (orderByClauses.length > 0) {
      const orders = orderByClauses.filter(o => o.column).map(o => `${o.column} ${o.direction}`);
      if (orders.length > 0) {
        query += `\nORDER BY ${orders.join(', ')}`;
      }
    }

    if (limit > 0) {
      query += `\nLIMIT ${limit}`;
    }

    return query;
  }, [selectedTable, selectedColumns, whereClauses, orderByClauses, limit]);

  function handleGenerateSql() {
    const generated = generateSql();
    setSql(generated);
    setActiveTab('sql');
  }

  function handleExecute() {
    if (!selectedDatasourceId || !sql.trim()) return;
    executionStartRef.current = Date.now();
    const paramMap: Record<string, string> = {};
    params.forEach(p => {
      if (p.name.trim()) paramMap[p.name.trim()] = p.value;
    });
    executeMutation.mutate({
      datasourceId: selectedDatasourceId,
      sql: sql.trim(),
      parameters: Object.keys(paramMap).length > 0 ? paramMap : undefined,
    });
  }

  function handleSaveQuery() {
    if (!selectedDatasourceId || !sql.trim() || !saveQueryName.trim()) return;
    saveQueryMutation.mutate({
      name: saveQueryName.trim(),
      sql: sql.trim(),
      datasourceId: selectedDatasourceId,
    });
  }

  function loadSavedQuery(query: SavedQuery) {
    setSelectedDatasourceId(query.datasourceId);
    setSql(query.sql);
    setActiveTab('sql');
    setStatusMessage(`Loaded query: ${query.name}`);
  }

  function exportCsv() {
    if (!queryResult) return;
    const header = queryResult.columns.join(',');
    const rows = queryResult.rows.map(row =>
      queryResult.columns.map(col => {
        const val = row[col];
        if (val === null || val === undefined) return '';
        const str = String(val);
        return str.includes(',') || str.includes('"') || str.includes('\n')
          ? `"${str.replace(/"/g, '""')}"`
          : str;
      }).join(',')
    );
    const csv = [header, ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'query_results.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Group tables by schema ───────────────────────────────────────────────

  const tablesBySchema = tables.reduce<Record<string, TableInfo[]>>((acc, t) => {
    if (!acc[t.schema]) acc[t.schema] = [];
    acc[t.schema].push(t);
    return acc;
  }, {});

  // ── Paginated results ────────────────────────────────────────────────────

  const pagedRows = queryResult
    ? queryResult.rows.slice((resultPage - 1) * resultPageSize, resultPage * resultPageSize)
    : [];
  const totalResultPages = queryResult
    ? Math.ceil(queryResult.rows.length / resultPageSize)
    : 0;

  // ── Datasource name for status bar ───────────────────────────────────────

  const selectedDatasourceName = connections.find(c => c.id === selectedDatasourceId)?.name;

  // ── Render ───────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col h-full">
      {/* Main Content Area */}
      <div className="flex flex-1 min-h-0">
        {/* Left Sidebar: Connection & Table Browser */}
        <aside className="w-72 border-r border-gray-200 bg-white flex flex-col shrink-0">
          <div className="p-3 border-b border-gray-200">
            <label className="block text-xs font-medium text-gray-500 uppercase tracking-wider mb-1">
              Datasource
            </label>
            <select
              value={selectedDatasourceId ?? ''}
              onChange={(e) => e.target.value && handleDatasourceChange(Number(e.target.value))}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="">Select a datasource...</option>
              {connections.map(c => (
                <option key={c.id} value={c.id}>{c.name} ({c.type})</option>
              ))}
            </select>
          </div>

          {/* Table Tree */}
          <div className="flex-1 overflow-y-auto p-2">
            {tablesLoading && (
              <div className="text-sm text-gray-400 p-2">Loading tables...</div>
            )}
            {!tablesLoading && selectedDatasourceId && tables.length === 0 && (
              <div className="text-sm text-gray-400 p-2">No tables found.</div>
            )}
            {Object.entries(tablesBySchema).map(([schema, schemaTables]) => (
              <div key={schema} className="mb-1">
                <button
                  onClick={() => toggleSchema(schema)}
                  className="flex items-center gap-1 w-full text-left px-2 py-1 text-xs font-semibold text-gray-600 hover:bg-gray-100 rounded"
                >
                  <span className="text-gray-400">{expandedSchemas.has(schema) ? '\u25BC' : '\u25B6'}</span>
                  {schema}
                  <span className="ml-auto text-gray-400">{schemaTables.length}</span>
                </button>
                {expandedSchemas.has(schema) && (
                  <div className="ml-3">
                    {schemaTables.map(t => (
                      <button
                        key={`${t.schema}.${t.name}`}
                        onClick={() => selectTable(t)}
                        className={`w-full text-left px-2 py-1 text-sm rounded truncate ${
                          selectedTable?.schema === t.schema && selectedTable?.name === t.name
                            ? 'bg-blue-50 text-blue-700 font-medium'
                            : 'text-gray-700 hover:bg-gray-50'
                        }`}
                      >
                        {t.name}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>

          {/* Columns panel */}
          {selectedTable && (
            <div className="border-t border-gray-200 max-h-64 overflow-y-auto">
              <div className="p-2">
                <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">
                  Columns: {selectedTable.schema}.{selectedTable.name}
                </h4>
                {columnsLoading ? (
                  <div className="text-sm text-gray-400 p-1">Loading...</div>
                ) : columns.length === 0 ? (
                  <div className="text-sm text-gray-400 p-1">No columns found.</div>
                ) : (
                  <div className="space-y-0.5">
                    {columns.map(col => (
                      <div
                        key={col.name}
                        className="flex items-center gap-2 px-1 py-0.5 text-xs hover:bg-gray-50 rounded group"
                      >
                        {col.primaryKey && (
                          <span className="text-yellow-500 font-bold" title="Primary Key">PK</span>
                        )}
                        {!col.primaryKey && <span className="w-5" />}
                        <span className="font-medium text-gray-800 truncate flex-1">{col.name}</span>
                        <span className="text-gray-400">{col.type}</span>
                        {col.nullable && (
                          <span className="text-gray-300" title="Nullable">?</span>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}
        </aside>

        {/* Main Area: Query Editor */}
        <div className="flex-1 flex flex-col min-w-0">
          {/* Tabs */}
          <div className="flex items-center border-b border-gray-200 bg-white px-4">
            {(['visual', 'sql', 'results'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setActiveTab(tab)}
                className={`px-4 py-3 text-sm font-medium border-b-2 transition-colors ${
                  activeTab === tab
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                }`}
              >
                {tab === 'visual' ? 'Visual Builder' : tab === 'sql' ? 'SQL Editor' : 'Results'}
              </button>
            ))}
            <div className="ml-auto flex items-center gap-2">
              <button
                onClick={() => setShowSavedQueries(!showSavedQueries)}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg border transition-colors ${
                  showSavedQueries
                    ? 'bg-blue-50 border-blue-200 text-blue-700'
                    : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                }`}
              >
                Saved Queries ({savedQueries.length})
              </button>
            </div>
          </div>

          <div className="flex flex-1 min-h-0">
            {/* Tab Content */}
            <div className="flex-1 overflow-auto">
              {/* Visual Builder */}
              {activeTab === 'visual' && (
                <div className="p-4 space-y-6">
                  {!selectedTable ? (
                    <div className="text-center text-gray-400 py-12">
                      Select a datasource and table from the left panel to begin building a query.
                    </div>
                  ) : (
                    <>
                      {/* Selected Columns */}
                      <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-2">SELECT Columns</h3>
                        <div className="bg-gray-50 rounded-lg p-3 border border-gray-200">
                          {columns.length === 0 ? (
                            <p className="text-sm text-gray-400">Loading columns...</p>
                          ) : (
                            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-1">
                              {columns.map(col => (
                                <label
                                  key={col.name}
                                  className="flex items-center gap-2 px-2 py-1 text-sm rounded hover:bg-white cursor-pointer"
                                >
                                  <input
                                    type="checkbox"
                                    checked={selectedColumns.has(col.name)}
                                    onChange={() => toggleColumn(col.name)}
                                    className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                  />
                                  <span className="truncate">{col.name}</span>
                                  <span className="text-gray-400 text-xs">{col.type}</span>
                                </label>
                              ))}
                            </div>
                          )}
                          {selectedColumns.size === 0 && columns.length > 0 && (
                            <p className="text-xs text-gray-400 mt-2">No columns selected = SELECT *</p>
                          )}
                        </div>
                      </div>

                      {/* WHERE Clauses */}
                      <div>
                        <div className="flex items-center justify-between mb-2">
                          <h3 className="text-sm font-semibold text-gray-700">WHERE Conditions</h3>
                          <button
                            onClick={addWhereClause}
                            className="text-xs text-blue-600 hover:text-blue-800 font-medium"
                          >
                            + Add Condition
                          </button>
                        </div>
                        {whereClauses.length === 0 ? (
                          <p className="text-sm text-gray-400">No conditions. Click "Add Condition" to filter results.</p>
                        ) : (
                          <div className="space-y-2">
                            {whereClauses.map((w, i) => (
                              <div key={i} className="flex items-center gap-2">
                                {i > 0 && <span className="text-xs text-gray-400 w-8">AND</span>}
                                {i === 0 && <span className="w-8" />}
                                <select
                                  value={w.column}
                                  onChange={(e) => updateWhereClause(i, 'column', e.target.value)}
                                  className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                                >
                                  {columns.map(c => (
                                    <option key={c.name} value={c.name}>{c.name}</option>
                                  ))}
                                </select>
                                <select
                                  value={w.operator}
                                  onChange={(e) => updateWhereClause(i, 'operator', e.target.value)}
                                  className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                                >
                                  {OPERATORS.map(op => (
                                    <option key={op} value={op}>{op}</option>
                                  ))}
                                </select>
                                {w.operator !== 'IS NULL' && w.operator !== 'IS NOT NULL' && (
                                  <input
                                    type="text"
                                    value={w.value}
                                    onChange={(e) => updateWhereClause(i, 'value', e.target.value)}
                                    placeholder="Value"
                                    className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 flex-1"
                                  />
                                )}
                                <button
                                  onClick={() => removeWhereClause(i)}
                                  className="text-red-500 hover:text-red-700 text-sm px-1"
                                  title="Remove condition"
                                >
                                  &times;
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>

                      {/* ORDER BY */}
                      <div>
                        <div className="flex items-center justify-between mb-2">
                          <h3 className="text-sm font-semibold text-gray-700">ORDER BY</h3>
                          <button
                            onClick={addOrderBy}
                            className="text-xs text-blue-600 hover:text-blue-800 font-medium"
                          >
                            + Add Sort
                          </button>
                        </div>
                        {orderByClauses.length === 0 ? (
                          <p className="text-sm text-gray-400">No sorting applied.</p>
                        ) : (
                          <div className="space-y-2">
                            {orderByClauses.map((o, i) => (
                              <div key={i} className="flex items-center gap-2">
                                <select
                                  value={o.column}
                                  onChange={(e) => updateOrderBy(i, 'column', e.target.value)}
                                  className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                                >
                                  {columns.map(c => (
                                    <option key={c.name} value={c.name}>{c.name}</option>
                                  ))}
                                </select>
                                <select
                                  value={o.direction}
                                  onChange={(e) => updateOrderBy(i, 'direction', e.target.value)}
                                  className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                                >
                                  <option value="ASC">ASC</option>
                                  <option value="DESC">DESC</option>
                                </select>
                                <button
                                  onClick={() => removeOrderBy(i)}
                                  className="text-red-500 hover:text-red-700 text-sm px-1"
                                  title="Remove sort"
                                >
                                  &times;
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>

                      {/* LIMIT */}
                      <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-2">LIMIT</h3>
                        <input
                          type="number"
                          value={limit}
                          onChange={(e) => setLimit(Number(e.target.value))}
                          min={0}
                          max={100000}
                          className="border border-gray-300 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 w-32"
                        />
                        <span className="text-xs text-gray-400 ml-2">0 = no limit</span>
                      </div>

                      {/* Generate SQL Button */}
                      <div>
                        <button
                          onClick={handleGenerateSql}
                          className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700"
                        >
                          Generate SQL
                        </button>
                      </div>
                    </>
                  )}
                </div>
              )}

              {/* SQL Editor */}
              {activeTab === 'sql' && (
                <div className="p-4 flex flex-col gap-4 h-full">
                  <div className="flex-1 min-h-0 flex flex-col">
                    <textarea
                      value={sql}
                      onChange={(e) => setSql(e.target.value)}
                      placeholder="Enter SQL query here..."
                      spellCheck={false}
                      className="flex-1 min-h-[200px] w-full border border-gray-300 rounded-lg px-4 py-3 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-blue-500 resize-y bg-gray-50"
                    />
                  </div>

                  {/* Parameters */}
                  <div>
                    <div className="flex items-center justify-between mb-2">
                      <h3 className="text-sm font-semibold text-gray-700">Parameters</h3>
                      <button
                        onClick={addParam}
                        className="text-xs text-blue-600 hover:text-blue-800 font-medium"
                      >
                        + Add Parameter
                      </button>
                    </div>
                    {params.length > 0 && (
                      <div className="space-y-2">
                        {params.map((p, i) => (
                          <div key={i} className="flex items-center gap-2">
                            <span className="text-sm text-gray-400">@</span>
                            <input
                              type="text"
                              value={p.name}
                              onChange={(e) => updateParam(i, 'name', e.target.value)}
                              placeholder="name"
                              className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 w-40"
                            />
                            <span className="text-sm text-gray-400">=</span>
                            <input
                              type="text"
                              value={p.value}
                              onChange={(e) => updateParam(i, 'value', e.target.value)}
                              placeholder="value"
                              className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 flex-1"
                            />
                            <button
                              onClick={() => removeParam(i)}
                              className="text-red-500 hover:text-red-700 text-sm px-1"
                            >
                              &times;
                            </button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  {/* Execute / Save buttons */}
                  <div className="flex items-center gap-3">
                    <button
                      onClick={handleExecute}
                      disabled={!selectedDatasourceId || !sql.trim() || executeMutation.isPending}
                      className="bg-green-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-green-700 disabled:opacity-50"
                    >
                      {executeMutation.isPending ? 'Executing...' : 'Execute'}
                    </button>
                    <button
                      onClick={() => { setSaveQueryName(''); setSaveModalOpen(true); }}
                      disabled={!selectedDatasourceId || !sql.trim()}
                      className="border border-gray-300 text-gray-700 px-4 py-2 rounded-lg text-sm hover:bg-gray-50 disabled:opacity-50"
                    >
                      Save Query
                    </button>
                    {executeMutation.isError && (
                      <Badge text="Error" variant="error" />
                    )}
                  </div>
                </div>
              )}

              {/* Results */}
              {activeTab === 'results' && (
                <div className="p-4 flex flex-col h-full">
                  {!queryResult ? (
                    <div className="text-center text-gray-400 py-12">
                      No results yet. Write and execute a query to see results here.
                    </div>
                  ) : queryResult.rows.length === 0 ? (
                    <div className="text-center text-gray-400 py-12">
                      Query executed successfully but returned no rows.
                      <div className="text-xs mt-1">Execution time: {queryResult.executionTimeMs}ms</div>
                    </div>
                  ) : (
                    <>
                      {/* Toolbar */}
                      <div className="flex items-center justify-between mb-3">
                        <div className="text-sm text-gray-600">
                          {queryResult.rowCount} row{queryResult.rowCount !== 1 ? 's' : ''} returned in {queryResult.executionTimeMs}ms
                        </div>
                        <button
                          onClick={exportCsv}
                          className="border border-gray-300 text-gray-700 px-3 py-1.5 rounded-lg text-xs hover:bg-gray-50"
                        >
                          Export CSV
                        </button>
                      </div>

                      {/* Results Table */}
                      <div className="flex-1 overflow-auto bg-white rounded-lg shadow border border-gray-200">
                        <table className="min-w-full divide-y divide-gray-200 text-sm">
                          <thead className="bg-gray-50 sticky top-0">
                            <tr>
                              <th className="px-3 py-2 text-left text-xs font-medium text-gray-400 w-12">#</th>
                              {queryResult.columns.map(col => (
                                <th key={col} className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider whitespace-nowrap">
                                  {col}
                                </th>
                              ))}
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-gray-100">
                            {pagedRows.map((row, idx) => (
                              <tr key={idx} className="hover:bg-gray-50">
                                <td className="px-3 py-1.5 text-xs text-gray-400">{(resultPage - 1) * resultPageSize + idx + 1}</td>
                                {queryResult.columns.map(col => (
                                  <td key={col} className="px-3 py-1.5 whitespace-nowrap max-w-xs truncate" title={String(row[col] ?? '')}>
                                    {row[col] === null ? (
                                      <span className="text-gray-300 italic">NULL</span>
                                    ) : (
                                      String(row[col])
                                    )}
                                  </td>
                                ))}
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>

                      {/* Pagination */}
                      {totalResultPages > 1 && (
                        <div className="flex items-center justify-between mt-3">
                          <p className="text-xs text-gray-500">
                            Showing {(resultPage - 1) * resultPageSize + 1} to{' '}
                            {Math.min(resultPage * resultPageSize, queryResult.rows.length)} of{' '}
                            {queryResult.rows.length}
                          </p>
                          <div className="flex gap-2">
                            <button
                              onClick={() => setResultPage(p => p - 1)}
                              disabled={resultPage <= 1}
                              className="px-3 py-1 text-xs border border-gray-300 rounded-lg disabled:opacity-50 hover:bg-gray-100"
                            >
                              Previous
                            </button>
                            <span className="px-3 py-1 text-xs text-gray-600">
                              Page {resultPage} of {totalResultPages}
                            </span>
                            <button
                              onClick={() => setResultPage(p => p + 1)}
                              disabled={resultPage >= totalResultPages}
                              className="px-3 py-1 text-xs border border-gray-300 rounded-lg disabled:opacity-50 hover:bg-gray-100"
                            >
                              Next
                            </button>
                          </div>
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>

            {/* Right Sidebar: Saved Queries */}
            {showSavedQueries && (
              <aside className="w-64 border-l border-gray-200 bg-white flex flex-col shrink-0">
                <div className="p-3 border-b border-gray-200">
                  <h3 className="text-sm font-semibold text-gray-700">Saved Queries</h3>
                </div>
                <div className="flex-1 overflow-y-auto p-2">
                  {savedQueries.length === 0 ? (
                    <p className="text-sm text-gray-400 p-2">No saved queries yet.</p>
                  ) : (
                    <div className="space-y-1">
                      {savedQueries.map(q => (
                        <div
                          key={q.id}
                          className="group p-2 rounded-lg hover:bg-gray-50 border border-transparent hover:border-gray-200"
                        >
                          <button
                            onClick={() => loadSavedQuery(q)}
                            className="w-full text-left"
                          >
                            <div className="text-sm font-medium text-gray-800 truncate">{q.name}</div>
                            <div className="text-xs text-gray-400 truncate mt-0.5 font-mono">{q.sql.slice(0, 60)}...</div>
                            <div className="text-xs text-gray-300 mt-0.5">{new Date(q.created).toLocaleDateString()}</div>
                          </button>
                          <button
                            onClick={() => deleteQueryMutation.mutate(q.id)}
                            className="text-xs text-red-500 hover:text-red-700 mt-1 opacity-0 group-hover:opacity-100 transition-opacity"
                          >
                            Delete
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </aside>
            )}
          </div>
        </div>
      </div>

      {/* Bottom Status Bar */}
      <div className="bg-gray-800 text-gray-300 px-4 py-2 text-xs flex items-center gap-4 shrink-0">
        {selectedDatasourceName ? (
          <>
            <span>Connected to <span className="text-white font-medium">{selectedDatasourceName}</span></span>
            <span className="text-gray-600">|</span>
          </>
        ) : (
          <span className="text-gray-500">No connection selected</span>
        )}
        {statusMessage && <span>{statusMessage}</span>}
        {executeMutation.isPending && (
          <span className="text-yellow-400">Executing query...</span>
        )}
      </div>

      {/* Save Query Modal */}
      <Modal isOpen={saveModalOpen} onClose={() => setSaveModalOpen(false)} title="Save Query">
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Query Name</label>
            <input
              type="text"
              value={saveQueryName}
              onChange={(e) => setSaveQueryName(e.target.value)}
              placeholder="My useful query"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">SQL Preview</label>
            <pre className="bg-gray-50 rounded-lg p-3 text-xs font-mono text-gray-600 overflow-x-auto max-h-32 border border-gray-200">
              {sql}
            </pre>
          </div>
          <div className="flex justify-end gap-3 pt-2">
            <button
              onClick={() => setSaveModalOpen(false)}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              onClick={handleSaveQuery}
              disabled={!saveQueryName.trim() || saveQueryMutation.isPending}
              className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
            >
              {saveQueryMutation.isPending ? 'Saving...' : 'Save'}
            </button>
          </div>
          {saveQueryMutation.isError && (
            <p className="text-red-600 text-sm">Failed to save query. Please try again.</p>
          )}
        </div>
      </Modal>
    </div>
  );
}
