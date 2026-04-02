import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import ProjectStatusChip from '../../components/common/ProjectStatusChip';
import type { ProjectStatus } from '../../types';

describe('ProjectStatusChip', () => {
  const statuses: ProjectStatus[] = ['Draft', 'Queued', 'PreparingMedia', 'Transcribing', 'Completed', 'Failed', 'Cancelled'];

  it.each(statuses)('renders %s status', (status) => {
    render(<ProjectStatusChip status={status} />);
    expect(screen.getByText(status === 'PreparingMedia' ? 'Preparing Media' : status)).toBeInTheDocument();
  });

  it('renders with medium size', () => {
    render(<ProjectStatusChip status="Completed" size="medium" />);
    expect(screen.getByText('Completed')).toBeInTheDocument();
  });
});
