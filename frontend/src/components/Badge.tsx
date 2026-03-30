interface BadgeProps {
  text: string;
  variant: 'success' | 'warning' | 'error' | 'info';
}

const variantClasses: Record<BadgeProps['variant'], string> = {
  success: 'bg-green-100 text-green-800',
  warning: 'bg-yellow-100 text-yellow-800',
  error: 'bg-red-100 text-red-800',
  info: 'bg-blue-100 text-blue-800',
};

export default function Badge({ text, variant }: BadgeProps) {
  return (
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${variantClasses[variant]}`}>
      {text}
    </span>
  );
}
