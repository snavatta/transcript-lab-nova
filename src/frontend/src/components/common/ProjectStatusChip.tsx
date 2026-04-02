import { Chip, type ChipProps } from '@mui/material';
import type { ProjectStatus } from '../../types';

const statusColors: Record<ProjectStatus, ChipProps['color']> = {
  Draft: 'default',
  Queued: 'info',
  PreparingMedia: 'warning',
  Transcribing: 'warning',
  Completed: 'success',
  Failed: 'error',
  Cancelled: 'default',
};

const statusLabels: Record<ProjectStatus, string> = {
  Draft: 'Draft',
  Queued: 'Queued',
  PreparingMedia: 'Preparing Media',
  Transcribing: 'Transcribing',
  Completed: 'Completed',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
};

interface Props {
  status: ProjectStatus;
  size?: ChipProps['size'];
}

export default function ProjectStatusChip({ status, size = 'small' }: Props) {
  return <Chip label={statusLabels[status]} color={statusColors[status]} size={size} variant="filled" />;
}
