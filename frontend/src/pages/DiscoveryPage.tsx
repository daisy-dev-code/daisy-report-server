import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import api from '../api/client';
import DataTable, { type Column } from '../components/DataTable';
import Modal from '../components/Modal';
import Badge from '../components/Badge';

/* ─── Types ─── */

interface DiscoveredService {
  serviceType: string;
  host: string;
  port: number;
  version?: string;
  serviceVersion?: string;
  latency?: number;
  latencyMs?: number;
  accessible?: boolean;
  isAccessible?: boolean;
  hostname?: string;
  serverName?: string;
  instanceName?: string;
  databases?: string[];
  connectionString?: string;
  errorMessage?: string;
}

interface ProbeResponse {
  host: string;
  isAlive: boolean;
  pingMs: number;
  hostname: string | null;
  services: DiscoveredService[];
}

interface ScanResponse {
  network: string;
  hosts: DiscoveredService[];
}

interface DnsResult {
  host: string;
  hostname: string;
  services: DiscoveredService[];
}

interface DnsResponse {
  domain: string;
  results: DnsResult[];
}

interface PortInfo {
  port: number;
  service: string;
  description: string;
}

interface DatasourceForm {
  name: string;
  host: string;
  port: number;
  serviceType: string;
  username: string;
  password: string;
  database: string;
  connectionString: string;
}

interface CheckResult {
  success: boolean;
  message?: string;
}

/* ─── Service colour helpers ─── */

const SERVICE_COLORS: Record<string, { bg: string; text: string; dot: string }> = {
  mysql:          { bg: 'bg-green-100',  text: 'text-green-800',  dot: 'bg-green-500' },
  mariadb:        { bg: 'bg-green-100',  text: 'text-green-800',  dot: 'bg-green-500' },
  postgresql:     { bg: 'bg-blue-100',   text: 'text-blue-800',   dot: 'bg-blue-500' },
  postgres:       { bg: 'bg-blue-100',   text: 'text-blue-800',   dot: 'bg-blue-500' },
  mssql:          { bg: 'bg-purple-100', text: 'text-purple-800', dot: 'bg-purple-500' },
  'sql server':   { bg: 'bg-purple-100', text: 'text-purple-800', dot: 'bg-purple-500' },
  sqlserver:      { bg: 'bg-purple-100', text: 'text-purple-800', dot: 'bg-purple-500' },
  oracle:         { bg: 'bg-orange-100', text: 'text-orange-800', dot: 'bg-orange-500' },
  mongodb:        { bg: 'bg-emerald-100',text: 'text-emerald-800',dot: 'bg-emerald-600' },
  redis:          { bg: 'bg-red-100',    text: 'text-red-800',    dot: 'bg-red-500' },
  elasticsearch:  { bg: 'bg-yellow-100', text: 'text-yellow-800', dot: 'bg-yellow-500' },
  clickhouse:     { bg: 'bg-amber-100',  text: 'text-amber-800',  dot: 'bg-amber-400' },
  http:           { bg: 'bg-gray-100',   text: 'text-gray-800',   dot: 'bg-gray-500' },
  rest:           { bg: 'bg-gray-100',   text: 'text-gray-800',   dot: 'bg-gray-500' },
};

const DEFAULT_COLOR = { bg: 'bg-gray-100', text: 'text-gray-700', dot: 'bg-gray-400' };

function serviceColor(type: string) {
  return SERVICE_COLORS[type.toLowerCase()] ?? DEFAULT_COLOR;
}

function ServiceBadge({ type }: { type: string }) {
  const c = serviceColor(type);
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium ${c.bg} ${c.text}`}>
      <span className={`w-2 h-2 rounded-full ${c.dot}`} />
      {type}
    </span>
  );
}

/* ─── Connection string builder ─── */

function buildConnectionString(svc: string, host: string, port: number, db: string, user: string): string {
  const s = svc.toLowerCase();
  if (s === 'mysql' || s === 'mariadb')
    return `jdbc:mysql://${host}:${port}/${db || 'mydb'}?user=${user || 'root'}`;
  if (s === 'postgresql' || s === 'postgres')
    return `jdbc:postgresql://${host}:${port}/${db || 'postgres'}?user=${user || 'postgres'}`;
  if (s === 'mssql' || s === 'sql server' || s === 'sqlserver')
    return `jdbc:sqlserver://${host}:${port};databaseName=${db || 'master'}`;
  if (s === 'oracle')
    return `jdbc:oracle:thin:@${host}:${port}:${db || 'orcl'}`;
  if (s === 'mongodb')
    return `mongodb://${user ? user + '@' : ''}${host}:${port}/${db || 'admin'}`;
  if (s === 'redis')
    return `redis://${host}:${port}`;
  if (s === 'elasticsearch')
    return `http://${host}:${port}`;
  if (s === 'clickhouse')
    return `jdbc:clickhouse://${host}:${port}/${db || 'default'}`;
  return `${svc.toLowerCase()}://${host}:${port}/${db || ''}`;
}

/* ─── Shared input classes ─── */

const inputCls = 'w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500';
const btnPrimary = 'bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50';
const btnSecondary = 'px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50';

/* ─── Skeleton cards ─── */

function SkeletonCards({ count = 3 }: { count?: number }) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="bg-white rounded-lg shadow p-4 animate-pulse">
          <div className="h-4 bg-gray-200 rounded w-1/3 mb-3" />
          <div className="h-3 bg-gray-200 rounded w-2/3 mb-2" />
          <div className="h-3 bg-gray-200 rounded w-1/2 mb-2" />
          <div className="h-3 bg-gray-200 rounded w-1/4" />
        </div>
      ))}
    </div>
  );
}

/* ─── Collapsible section wrapper ─── */

function Section({ title, defaultOpen = true, children }: { title: string; defaultOpen?: boolean; children: React.ReactNode }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className="bg-white rounded-lg shadow">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between px-6 py-4 text-left"
      >
        <h3 className="text-lg font-semibold text-gray-900">{title}</h3>
        <svg
          className={`w-5 h-5 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`}
          fill="none" viewBox="0 0 24 24" stroke="currentColor"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      {open && <div className="px-6 pb-6">{children}</div>}
    </div>
  );
}

/* ─── Page ─── */

export default function DiscoveryPage() {
  /* --- Quick Probe state --- */
  const [probeHost, setProbeHost] = useState('');
  const [probeUser, setProbeUser] = useState('');
  const [probePass, setProbePass] = useState('');
  const [probeLoading, setProbeLoading] = useState(false);
  const [probeResults, setProbeResults] = useState<ProbeResponse | null>(null);
  const [probeError, setProbeError] = useState('');

  /* --- Network Scan state --- */
  const [scanRange, setScanRange] = useState('');
  const [scanParallel, setScanParallel] = useState(50);
  const [scanLoading, setScanLoading] = useState(false);
  const [scanResults, setScanResults] = useState<DiscoveredService[] | null>(null);
  const [scanError, setScanError] = useState('');

  /* --- DNS Discovery state --- */
  const [dnsDomain, setDnsDomain] = useState('');
  const [dnsLoading, setDnsLoading] = useState(false);
  const [dnsResults, setDnsResults] = useState<DnsResult[] | null>(null);
  const [dnsError, setDnsError] = useState('');

  /* --- Add Datasource Modal state --- */
  const [modalOpen, setModalOpen] = useState(false);
  const [dsForm, setDsForm] = useState<DatasourceForm>({
    name: '', host: '', port: 3306, serviceType: 'MySQL',
    username: '', password: '', database: '', connectionString: '',
  });
  const [dsDiscoveredDbs, setDsDiscoveredDbs] = useState<string[]>([]);
  const [testStatus, setTestStatus] = useState<'idle' | 'testing' | 'success' | 'error'>('idle');
  const [testMessage, setTestMessage] = useState('');
  const [createStatus, setCreateStatus] = useState<'idle' | 'saving' | 'success' | 'error'>('idle');
  const [createdDsId, setCreatedDsId] = useState<number | null>(null);

  /* --- Well-Known Ports query --- */
  const portsQuery = useQuery({
    queryKey: ['discovery-ports'],
    queryFn: () => api.get('/discovery/ports').then(r => (r.data?.data ?? r.data?.ports ?? r.data ?? []) as PortInfo[]),
    staleTime: 60_000 * 10,
  });

  /* ── Actions ── */

  async function handleProbe() {
    if (!probeHost.trim()) return;
    setProbeLoading(true);
    setProbeError('');
    setProbeResults(null);
    try {
      const res = await api.post<ProbeResponse>('/discovery/probe', {
        host: probeHost.trim(),
        username: probeUser || undefined,
        password: probePass || undefined,
      });
      setProbeResults(res.data);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
      setProbeError(msg ?? 'Probe failed. Check the host and try again.');
    } finally {
      setProbeLoading(false);
    }
  }

  async function handleScan() {
    if (!scanRange.trim()) return;
    setScanLoading(true);
    setScanError('');
    setScanResults(null);
    try {
      const res = await api.post<ScanResponse>('/discovery/scan', {
        network: scanRange.trim(),
        maxParallel: scanParallel,
      });
      setScanResults(res.data.hosts ?? []);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
      setScanError(msg ?? 'Network scan failed.');
    } finally {
      setScanLoading(false);
    }
  }

  async function handleDns() {
    if (!dnsDomain.trim()) return;
    setDnsLoading(true);
    setDnsError('');
    setDnsResults(null);
    try {
      const res = await api.post<DnsResponse>('/discovery/dns', {
        domain: dnsDomain.trim(),
      });
      setDnsResults(res.data.results ?? []);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
      setDnsError(msg ?? 'DNS discovery failed.');
    } finally {
      setDnsLoading(false);
    }
  }

  function openAddDatasource(svc: DiscoveredService) {
    const form: DatasourceForm = {
      name: svc.serverName ?? `${svc.serviceType} on ${svc.host}`,
      host: svc.host,
      port: svc.port,
      serviceType: svc.serviceType,
      username: probeUser || '',
      password: '',
      database: '',
      connectionString: svc.connectionString ?? buildConnectionString(svc.serviceType, svc.host, svc.port, '', ''),
    };
    setDsForm(form);
    setDsDiscoveredDbs(svc.databases ?? []);
    setTestStatus('idle');
    setTestMessage('');
    setCreateStatus('idle');
    setCreatedDsId(null);
    setModalOpen(true);
  }

  function updateDsForm(patch: Partial<DatasourceForm>) {
    setDsForm(prev => {
      const next = { ...prev, ...patch };
      // Auto-regenerate connection string unless user is editing it directly
      if (!('connectionString' in patch)) {
        next.connectionString = buildConnectionString(
          next.serviceType, next.host, next.port, next.database, next.username,
        );
      }
      return next;
    });
  }

  async function handleTestConnection() {
    setTestStatus('testing');
    setTestMessage('');
    try {
      const res = await api.post<CheckResult>('/discovery/check', {
        host: dsForm.host,
        port: dsForm.port,
        serviceType: dsForm.serviceType,
        username: dsForm.username || undefined,
        password: dsForm.password || undefined,
        database: dsForm.database || undefined,
      });
      if (res.data.success) {
        setTestStatus('success');
        setTestMessage(res.data.message ?? 'Connection successful');
      } else {
        setTestStatus('error');
        setTestMessage(res.data.message ?? 'Connection failed');
      }
    } catch {
      setTestStatus('error');
      setTestMessage('Connection test failed');
    }
  }

  async function handleCreateDatasource(e: React.FormEvent) {
    e.preventDefault();
    setCreateStatus('saving');
    try {
      const res = await api.post<{ id: number }>('/discovery/create-datasource', {
        name: dsForm.name,
        host: dsForm.host,
        port: dsForm.port,
        serviceType: dsForm.serviceType,
        username: dsForm.username || undefined,
        password: dsForm.password || undefined,
        database: dsForm.database || undefined,
        connectionString: dsForm.connectionString,
      });
      setCreateStatus('success');
      setCreatedDsId(res.data.id);
    } catch {
      setCreateStatus('error');
    }
  }

  /* ── Scan results table columns ── */

  const scanColumns: Column<DiscoveredService>[] = [
    { key: 'host', header: 'Host', render: (s) => <span className="font-medium text-gray-900">{s.serverName ?? s.host}</span> },
    { key: 'port', header: 'Port', render: (s) => <span className="font-mono text-xs">{s.port}</span> },
    { key: 'serviceType', header: 'Service', render: (s) => <ServiceBadge type={s.serviceType} /> },
    { key: 'version', header: 'Version', render: (s) => s.serviceVersion ?? s.version ?? '--' },
    {
      key: 'latency', header: 'Latency',
      render: (s) => s.latency != null ? <span className="text-xs text-gray-600">{s.latency}ms</span> : <span className="text-gray-400">--</span>,
    },
    {
      key: 'status', header: 'Status',
      render: (s) => <Badge text={s.accessible ? 'Accessible' : 'Unreachable'} variant={s.accessible ? 'success' : 'error'} />,
    },
    {
      key: 'actions', header: '', className: 'w-36',
      render: (s) => s.accessible ? (
        <button onClick={() => openAddDatasource(s)} className="text-blue-600 hover:text-blue-800 text-sm font-medium">
          Add as Datasource
        </button>
      ) : null,
    },
  ];

  /* ── Port reference table columns ── */

  const portColumns: Column<PortInfo>[] = [
    { key: 'port', header: 'Port', render: (p) => <span className="font-mono text-sm">{p.port}</span> },
    { key: 'service', header: 'Service', render: (p) => <ServiceBadge type={p.service} /> },
    { key: 'description', header: 'Description' },
  ];

  /* ── Render ── */

  return (
    <div className="p-6 space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900">Discovery</h2>
        <p className="text-sm text-gray-500 mt-1">Probe hosts, scan networks, and discover datasources automatically.</p>
      </div>

      {/* ── Section 1: Quick Probe ── */}
      <Section title="Quick Probe">
        <div className="flex flex-wrap gap-3 items-end">
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">Host / IP</label>
            <input
              type="text"
              value={probeHost}
              onChange={(e) => setProbeHost(e.target.value)}
              placeholder="192.168.1.1 or db.company.local"
              className={inputCls}
              onKeyDown={(e) => e.key === 'Enter' && handleProbe()}
            />
          </div>
          <div className="w-40">
            <label className="block text-sm font-medium text-gray-700 mb-1">Username <span className="text-gray-400 font-normal">(optional)</span></label>
            <input
              type="text"
              value={probeUser}
              onChange={(e) => setProbeUser(e.target.value)}
              className={inputCls}
            />
          </div>
          <div className="w-40">
            <label className="block text-sm font-medium text-gray-700 mb-1">Password <span className="text-gray-400 font-normal">(optional)</span></label>
            <input
              type="password"
              value={probePass}
              onChange={(e) => setProbePass(e.target.value)}
              className={inputCls}
            />
          </div>
          <button onClick={handleProbe} disabled={probeLoading || !probeHost.trim()} className={btnPrimary}>
            {probeLoading ? 'Probing...' : 'Probe Host'}
          </button>
        </div>

        {probeError && <p className="mt-3 text-sm text-red-600">{probeError}</p>}

        {probeLoading && <div className="mt-4"><SkeletonCards count={3} /></div>}

        {probeResults !== null && !probeLoading && (
          <div className="mt-4">
            {/* Host info banner */}
            <div className="bg-gray-50 border border-gray-200 rounded-lg p-4 mb-4">
              <div className="flex items-center gap-3">
                <span className={`w-3 h-3 rounded-full ${probeResults.isAlive ? 'bg-green-500' : 'bg-red-500'}`} />
                <div>
                  <p className="font-medium text-gray-900">
                    {probeResults.hostname ? `${probeResults.hostname} (${probeResults.host})` : probeResults.host}
                  </p>
                  <p className="text-xs text-gray-500">
                    {probeResults.isAlive ? `Host is alive (${probeResults.pingMs}ms)` : 'Host did not respond to ping'}
                    {probeResults.services.length > 0
                      ? ` - ${probeResults.services.length} database service(s) found`
                      : ' - No accessible database services detected'}
                  </p>
                </div>
              </div>
            </div>

            {probeResults.services.length === 0 ? (
              <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
                <p className="text-sm font-medium text-yellow-800">No accessible database services found</p>
                <p className="text-xs text-yellow-700 mt-1">
                  Scanned 18 database ports (MySQL, PostgreSQL, SQL Server, Oracle, MongoDB, Redis, Elasticsearch, etc.)
                  and tried direct connections via hostname. The database may be behind a firewall, require VPN access,
                  or use a non-standard port. Try providing credentials or check with your network administrator.
                </p>
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {probeResults.services.map((svc, i) => (
                  <div key={i} className="bg-gray-50 border border-gray-200 rounded-lg p-4 flex flex-col gap-2">
                    <div className="flex items-center justify-between">
                      <ServiceBadge type={svc.serviceType} />
                      <Badge text={(svc.accessible ?? svc.isAccessible) ? 'Accessible' : 'Discovered'} variant={(svc.accessible ?? svc.isAccessible) ? 'success' : 'info'} />
                    </div>
                    {svc.serverName && <p className="font-medium text-sm text-gray-900">{svc.serverName}</p>}
                    <p className="font-mono text-xs text-gray-600">{svc.host}:{svc.port}</p>
                    {(svc.version || svc.serviceVersion) && <p className="text-xs text-gray-500">Version: {svc.serviceVersion ?? svc.version}</p>}
                    {(svc.latency ?? svc.latencyMs) != null && (svc.latency ?? svc.latencyMs)! > 0 && <p className="text-xs text-gray-500">Latency: {svc.latency ?? svc.latencyMs}ms</p>}
                    {svc.connectionString && <p className="font-mono text-xs text-gray-400 truncate" title={svc.connectionString}>{svc.connectionString}</p>}
                    {svc.errorMessage && <p className="text-xs text-yellow-600">{svc.errorMessage}</p>}
                    {svc.accessible && (
                      <button
                        onClick={() => openAddDatasource(svc)}
                        className="mt-auto text-sm text-blue-600 hover:text-blue-800 font-medium text-left"
                      >
                        Add as Datasource
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </Section>

      {/* ── Section 2: Network Scan ── */}
      <Section title="Network Scan" defaultOpen={false}>
        <div className="flex flex-wrap gap-3 items-end">
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">Network Range</label>
            <input
              type="text"
              value={scanRange}
              onChange={(e) => setScanRange(e.target.value)}
              placeholder="192.168.1.0/24"
              className={inputCls}
              onKeyDown={(e) => e.key === 'Enter' && handleScan()}
            />
          </div>
          <div className="w-56">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Max Parallel: <span className="font-mono">{scanParallel}</span>
            </label>
            <input
              type="range"
              min={1}
              max={200}
              value={scanParallel}
              onChange={(e) => setScanParallel(Number(e.target.value))}
              className="w-full accent-blue-600"
            />
          </div>
          <button onClick={handleScan} disabled={scanLoading || !scanRange.trim()} className={btnPrimary}>
            {scanLoading ? 'Scanning...' : 'Scan Network'}
          </button>
        </div>

        {scanLoading && (
          <div className="mt-4 flex items-center gap-3">
            <div className="w-5 h-5 border-2 border-blue-600 border-t-transparent rounded-full animate-spin" />
            <span className="text-sm text-gray-600">Scanning network, this may take a moment...</span>
          </div>
        )}

        {scanError && <p className="mt-3 text-sm text-red-600">{scanError}</p>}

        {scanResults !== null && !scanLoading && (
          <div className="mt-4">
            {scanResults.length === 0 ? (
              <p className="text-sm text-gray-500">No services discovered on this network range.</p>
            ) : (
              <DataTable
                columns={scanColumns}
                data={scanResults}
              />
            )}
          </div>
        )}
      </Section>

      {/* ── Section 3: DNS Discovery ── */}
      <Section title="DNS Discovery" defaultOpen={false}>
        <div className="flex flex-wrap gap-3 items-end">
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">Domain</label>
            <input
              type="text"
              value={dnsDomain}
              onChange={(e) => setDnsDomain(e.target.value)}
              placeholder="company.local or daisy.co.za"
              className={inputCls}
              onKeyDown={(e) => e.key === 'Enter' && handleDns()}
            />
          </div>
          <button onClick={handleDns} disabled={dnsLoading || !dnsDomain.trim()} className={btnPrimary}>
            {dnsLoading ? 'Discovering...' : 'Discover'}
          </button>
        </div>

        {dnsLoading && <div className="mt-4"><SkeletonCards count={2} /></div>}

        {dnsError && <p className="mt-3 text-sm text-red-600">{dnsError}</p>}

        {dnsResults !== null && !dnsLoading && (
          <div className="mt-4">
            {dnsResults.length === 0 ? (
              <p className="text-sm text-gray-500">No DNS results for this domain.</p>
            ) : (
              <div className="space-y-4">
                {dnsResults.map((result, ri) => (
                  <div key={ri} className="border border-gray-200 rounded-lg overflow-hidden">
                    <div className="bg-gray-50 px-4 py-3 border-b border-gray-200">
                      <p className="font-medium text-sm text-gray-900">{result.hostname || result.host}</p>
                      <p className="text-xs text-gray-500 font-mono">{result.host}</p>
                    </div>
                    {result.services.length === 0 ? (
                      <p className="px-4 py-3 text-sm text-gray-500">No services found</p>
                    ) : (
                      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3 p-4">
                        {result.services.map((svc, si) => (
                          <div key={si} className="bg-gray-50 rounded-lg p-3 flex flex-col gap-1.5">
                            <div className="flex items-center justify-between">
                              <ServiceBadge type={svc.serviceType} />
                              <Badge text={svc.accessible ? 'Accessible' : 'Unreachable'} variant={svc.accessible ? 'success' : 'error'} />
                            </div>
                            <p className="font-mono text-xs text-gray-700">:{svc.port}</p>
                            {svc.version && <p className="text-xs text-gray-500">{svc.version}</p>}
                            {svc.latency != null && <p className="text-xs text-gray-500">{svc.latency}ms</p>}
                            {svc.accessible && (
                              <button
                                onClick={() => openAddDatasource({ ...svc, host: result.host })}
                                className="text-xs text-blue-600 hover:text-blue-800 font-medium text-left mt-1"
                              >
                                Add as Datasource
                              </button>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </Section>

      {/* ── Section 4: Well-Known Ports Reference ── */}
      <Section title="Well-Known Ports Reference" defaultOpen={false}>
        <DataTable
          columns={portColumns}
          data={portsQuery.data ?? []}
          loading={portsQuery.isLoading}
        />
      </Section>

      {/* ── Add as Datasource Modal ── */}
      <Modal isOpen={modalOpen} onClose={() => setModalOpen(false)} title="Add as Datasource" wide>
        {createStatus === 'success' ? (
          <div className="text-center py-4">
            <div className="w-12 h-12 rounded-full bg-green-100 flex items-center justify-center mx-auto mb-3">
              <svg className="w-6 h-6 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
              </svg>
            </div>
            <p className="text-lg font-semibold text-gray-900 mb-1">Datasource Created</p>
            <p className="text-sm text-gray-500 mb-4">Your new datasource has been saved successfully.</p>
            <div className="flex justify-center gap-3">
              {createdDsId && (
                <a
                  href="/datasources"
                  className="text-sm text-blue-600 hover:text-blue-800 font-medium"
                >
                  View Datasources
                </a>
              )}
              <button onClick={() => setModalOpen(false)} className={btnSecondary}>
                Close
              </button>
            </div>
          </div>
        ) : (
          <form onSubmit={handleCreateDatasource} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
              <input
                type="text"
                value={dsForm.name}
                onChange={(e) => updateDsForm({ name: e.target.value })}
                className={inputCls}
                required
              />
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div className="col-span-2">
                <label className="block text-sm font-medium text-gray-700 mb-1">Host</label>
                <input
                  type="text"
                  value={dsForm.host}
                  onChange={(e) => updateDsForm({ host: e.target.value })}
                  className={inputCls}
                  required
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Port</label>
                <input
                  type="number"
                  value={dsForm.port}
                  onChange={(e) => updateDsForm({ port: Number(e.target.value) })}
                  className={inputCls}
                  required
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Service Type</label>
              <select
                value={dsForm.serviceType}
                onChange={(e) => updateDsForm({ serviceType: e.target.value })}
                className={inputCls}
              >
                <option value="MySQL">MySQL</option>
                <option value="MariaDB">MariaDB</option>
                <option value="PostgreSQL">PostgreSQL</option>
                <option value="SQL Server">SQL Server</option>
                <option value="Oracle">Oracle</option>
                <option value="MongoDB">MongoDB</option>
                <option value="Redis">Redis</option>
                <option value="Elasticsearch">Elasticsearch</option>
                <option value="ClickHouse">ClickHouse</option>
                <option value="HTTP">HTTP / REST</option>
              </select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Username</label>
                <input
                  type="text"
                  value={dsForm.username}
                  onChange={(e) => updateDsForm({ username: e.target.value })}
                  className={inputCls}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Password</label>
                <input
                  type="password"
                  value={dsForm.password}
                  onChange={(e) => updateDsForm({ password: e.target.value })}
                  className={inputCls}
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Database</label>
              {dsDiscoveredDbs.length > 0 ? (
                <div className="flex gap-2">
                  <select
                    value={dsForm.database}
                    onChange={(e) => updateDsForm({ database: e.target.value })}
                    className={`${inputCls} flex-1`}
                  >
                    <option value="">-- Select or type --</option>
                    {dsDiscoveredDbs.map((db) => (
                      <option key={db} value={db}>{db}</option>
                    ))}
                  </select>
                  <input
                    type="text"
                    value={dsForm.database}
                    onChange={(e) => updateDsForm({ database: e.target.value })}
                    placeholder="Or type name..."
                    className={`${inputCls} flex-1`}
                  />
                </div>
              ) : (
                <input
                  type="text"
                  value={dsForm.database}
                  onChange={(e) => updateDsForm({ database: e.target.value })}
                  placeholder="e.g., mydb"
                  className={inputCls}
                />
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Connection String</label>
              <input
                type="text"
                value={dsForm.connectionString}
                onChange={(e) => updateDsForm({ connectionString: e.target.value })}
                className={`${inputCls} font-mono text-xs`}
              />
            </div>

            {/* Test Connection */}
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={handleTestConnection}
                disabled={testStatus === 'testing'}
                className={btnSecondary}
              >
                {testStatus === 'testing' ? 'Testing...' : 'Test Connection'}
              </button>
              {testStatus === 'success' && <Badge text="Connected" variant="success" />}
              {testStatus === 'error' && <span className="text-xs text-red-600">{testMessage}</span>}
              {testStatus === 'success' && <span className="text-xs text-green-600">{testMessage}</span>}
            </div>

            <div className="flex justify-end gap-3 pt-2 border-t border-gray-100">
              <button type="button" onClick={() => setModalOpen(false)} className={btnSecondary}>
                Cancel
              </button>
              <button type="submit" disabled={createStatus === 'saving'} className={btnPrimary}>
                {createStatus === 'saving' ? 'Creating...' : 'Create Datasource'}
              </button>
            </div>

            {createStatus === 'error' && (
              <p className="text-red-600 text-sm">Failed to create datasource. Please try again.</p>
            )}
          </form>
        )}
      </Modal>
    </div>
  );
}
