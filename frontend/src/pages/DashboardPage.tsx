export default function DashboardPage() {
  return (
    <div className="p-6">
      <h2 className="text-2xl font-bold mb-4">Dashboard</h2>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { label: 'Reports', value: '0', color: 'bg-blue-500' },
          { label: 'Datasources', value: '0', color: 'bg-green-500' },
          { label: 'Scheduled Jobs', value: '0', color: 'bg-purple-500' },
          { label: 'Users', value: '0', color: 'bg-orange-500' },
        ].map((stat) => (
          <div key={stat.label} className="bg-white rounded-lg shadow p-6">
            <div className={`w-10 h-10 ${stat.color} rounded-lg mb-3`} />
            <p className="text-sm text-gray-500">{stat.label}</p>
            <p className="text-3xl font-bold">{stat.value}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
