/**
 * Power BI Embedded Report Viewer
 *
 * NOTE: This uses a simple iframe approach with the embed URL and access token.
 * For full interactive embedding with features like page navigation, filters, and
 * event handling, install the `powerbi-client` npm package and use the PowerBI
 * JavaScript SDK instead of a raw iframe.
 *
 *   npm install powerbi-client
 *
 * See: https://learn.microsoft.com/en-us/javascript/api/overview/powerbi/
 */

import { useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import api from '../api/client';

interface EmbedInfo {
  embedUrl: string;
  embedToken: string;
  reportName: string;
  pages?: { name: string; displayName: string }[];
}

export default function PowerBiEmbedPage() {
  const { workspaceId, reportId } = useParams<{ workspaceId: string; reportId: string }>();
  const navigate = useNavigate();
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [selectedPage, setSelectedPage] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);

  const embedQuery = useQuery({
    queryKey: ['pbi-embed', workspaceId, reportId, refreshKey],
    queryFn: () =>
      api
        .post<EmbedInfo>(`/powerbi/workspaces/${workspaceId}/reports/${reportId}/embed`)
        .then((r) => r.data),
    enabled: !!workspaceId && !!reportId,
  });

  const handleRefresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
  }, []);

  const toggleFullscreen = useCallback(() => {
    setIsFullscreen((f) => !f);
  }, []);

  const embedUrl = embedQuery.data
    ? `${embedQuery.data.embedUrl}${embedQuery.data.embedUrl.includes('?') ? '&' : '?'}accessToken=${encodeURIComponent(embedQuery.data.embedToken)}${selectedPage ? `&pageName=${encodeURIComponent(selectedPage)}` : ''}`
    : '';

  if (embedQuery.isError) {
    return (
      <div className="p-6">
        <div className="bg-white rounded-lg shadow p-8 text-center">
          <p className="text-red-600 mb-4">Failed to load embed information for this report.</p>
          <button
            onClick={() => navigate('/powerbi')}
            className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            Back to Power BI
          </button>
        </div>
      </div>
    );
  }

  const containerCls = isFullscreen
    ? 'fixed inset-0 z-50 bg-white flex flex-col'
    : 'p-6 flex flex-col h-full';

  return (
    <div className={containerCls}>
      {/* Toolbar */}
      <div className="flex items-center justify-between mb-3 px-2 flex-shrink-0">
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate('/powerbi')}
            className="text-gray-600 hover:text-gray-900 text-sm font-medium"
          >
            &larr; Back
          </button>
          <h2 className="text-lg font-semibold truncate">
            {embedQuery.data?.reportName ?? 'Loading report...'}
          </h2>
        </div>
        <div className="flex items-center gap-3">
          {/* Page selector */}
          {embedQuery.data?.pages && embedQuery.data.pages.length > 1 && (
            <select
              value={selectedPage}
              onChange={(e) => setSelectedPage(e.target.value)}
              className="border border-gray-300 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="">Default Page</option>
              {embedQuery.data.pages.map((p) => (
                <option key={p.name} value={p.name}>
                  {p.displayName}
                </option>
              ))}
            </select>
          )}
          <button
            onClick={handleRefresh}
            disabled={embedQuery.isFetching}
            className="px-3 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
          >
            {embedQuery.isFetching ? 'Loading...' : 'Refresh'}
          </button>
          <button
            onClick={toggleFullscreen}
            className="px-3 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50"
          >
            {isFullscreen ? 'Exit Fullscreen' : 'Fullscreen'}
          </button>
        </div>
      </div>

      {/* Embed area */}
      <div className="flex-1 bg-gray-100 rounded-lg overflow-hidden min-h-0">
        {embedQuery.isLoading || embedQuery.isFetching ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <div className="h-8 w-8 border-2 border-blue-600 border-t-transparent rounded-full animate-spin mx-auto mb-3" />
              <p className="text-sm text-gray-500">Loading embedded report...</p>
            </div>
          </div>
        ) : embedUrl ? (
          <iframe
            key={refreshKey}
            src={embedUrl}
            title={embedQuery.data?.reportName ?? 'Power BI Report'}
            className="w-full h-full border-0"
            allowFullScreen
          />
        ) : null}
      </div>
    </div>
  );
}
