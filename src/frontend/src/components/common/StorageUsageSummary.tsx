import { Stack, Typography } from '@mui/material';
import { formatBytes } from '../../utils/format';

interface Props {
  originalFileSizeBytes?: number | null;
  workspaceSizeBytes?: number | null;
  totalSizeBytes?: number | null;
  compact?: boolean;
}

export default function StorageUsageSummary({ originalFileSizeBytes, workspaceSizeBytes, totalSizeBytes, compact }: Props) {
  const total = totalSizeBytes ?? workspaceSizeBytes;
  if (total == null && originalFileSizeBytes == null) return null;

  if (compact) {
    return (
      <Typography variant="body2" color="text.secondary">
        {formatBytes(total ?? originalFileSizeBytes)}
      </Typography>
    );
  }

  return (
    <Stack spacing={0.5}>
      {originalFileSizeBytes != null && (
        <Typography variant="body2" color="text.secondary">
          Original: {formatBytes(originalFileSizeBytes)}
        </Typography>
      )}
      {workspaceSizeBytes != null && workspaceSizeBytes !== originalFileSizeBytes && (
        <Typography variant="body2" color="text.secondary">
          Workspace: {formatBytes(workspaceSizeBytes)}
        </Typography>
      )}
      {total != null && (
        <Typography variant="body2" color="text.secondary">
          Total: {formatBytes(total)}
        </Typography>
      )}
    </Stack>
  );
}
