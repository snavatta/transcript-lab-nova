import { Box, Button, Chip, Paper, Skeleton, Typography } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import { useSystemCapabilities } from '../../hooks/useData';
import type { ComputeBackendCapabilityDto, HostedProviderCapabilityDto } from '../../types';
import { formatDate } from '../../utils/format';
import EmptyState from '../common/EmptyState';

const EXPECTED_BACKENDS = ['CPU', 'CUDA', 'CoreML', 'OpenVINO'] as const;

function getProviderStatus(provider: HostedProviderCapabilityDto): string {
  if (provider.status === 'Not configured.' || provider.status === 'Reachable.' || provider.status === 'Configured but unreachable.') {
    return provider.status;
  }

  return 'Status reported.';
}

function getBackendStatus(backend: ComputeBackendCapabilityDto | undefined): string {
  if (backend?.status === 'Available.' || backend?.status === 'Unavailable.') {
    return backend.status;
  }

  return backend ? 'Status reported.' : 'Not reported.';
}

function getSafeDeviceLabel(backend: string, devices: string[]): string {
  const allowedLabels: Record<string, readonly string[]> = {
    CPU: ['CPU device'],
    CUDA: ['CUDA device'],
    CoreML: ['Apple Silicon'],
    OpenVINO: ['OpenVINO device'],
  };
  const knownDeviceCount = devices.filter((device) => allowedLabels[backend]?.includes(device)).length;

  return knownDeviceCount === 0
    ? 'No device details reported.'
    : `${knownDeviceCount} sanitized device${knownDeviceCount === 1 ? '' : 's'} reported.`;
}

export function SystemCapabilitiesPanel() {
  const { data, error, isLoading, mutate } = useSystemCapabilities(true);

  if (isLoading) {
    return (
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
        <Skeleton variant="rounded" height={188} />
        <Skeleton variant="rounded" height={188} />
        <Skeleton variant="rounded" height={188} />
        <Skeleton variant="rounded" height={188} />
      </Box>
    );
  }

  if (error) {
    return (
      <EmptyState
        title="Unable to load system capabilities."
        description="The sanitized runtime summary could not be loaded. Try again to request a fresh summary."
        action={<Button variant="outlined" startIcon={<RefreshIcon />} onClick={() => void mutate()} aria-label="Refresh capabilities">Refresh</Button>}
      />
    );
  }

  if (!data || (data.hostedProviders.length === 0 && data.computeBackends.length === 0)) {
    return (
      <EmptyState
        title="No system capabilities reported."
        description="The runtime did not return any sanitized provider or compute capability details."
        action={<Button variant="outlined" startIcon={<RefreshIcon />} onClick={() => void mutate()} aria-label="Refresh capabilities">Refresh</Button>}
      />
    );
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Box sx={{ display: 'flex', alignItems: { xs: 'flex-start', sm: 'center' }, justifyContent: 'space-between', gap: 2, flexDirection: { xs: 'column', sm: 'row' } }}>
        <Box>
          <Typography variant="subtitle1" fontWeight={600}>System Capabilities</Typography>
          <Typography variant="body2" color="text.secondary">Collected {formatDate(data.collectedAtUtc)}. This summary excludes credentials, URLs, and private device identifiers.</Typography>
        </Box>
        <Button variant="outlined" startIcon={<RefreshIcon />} onClick={() => void mutate()} aria-label="Refresh capabilities">Refresh</Button>
      </Box>

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>Hosted providers</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          {data.hostedProviders.map((provider) => (
            <Box key={provider.provider} sx={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 1 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="body2" fontWeight={600}>{provider.provider}</Typography>
                <Chip size="small" variant="outlined" color={provider.configured ? 'success' : 'default'} label={provider.configured ? 'Configured' : 'Not configured'} />
              </Box>
              <Typography variant="caption" color="text.secondary">
                Reachability: {provider.reachable === true ? 'Reachable' : provider.reachable === false ? 'Unreachable' : 'Not checked'} · {getProviderStatus(provider)}
              </Typography>
            </Box>
          ))}
        </Box>
      </Paper>

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>CPU summary</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
          <Typography variant="body2">Architecture: {data.architecture}</Typography>
          <Typography variant="body2">Logical processors: {data.logicalProcessorCount}</Typography>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Operating system: {data.osDescription}</Typography>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Hardware name: {data.hardwareName ?? 'Not reported'}</Typography>
        </Box>
      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
        {EXPECTED_BACKENDS.map((backendName) => {
          const backend = data.computeBackends.find((candidate) => candidate.backend === backendName);
          const available = backend?.available === true;
          return (
            <Paper key={backendName} variant="outlined" sx={{ p: { xs: 2, sm: 2.5 }, minWidth: 0 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', mb: 1 }}>
                <Typography variant="subtitle2">{backendName}</Typography>
                <Chip size="small" variant="outlined" color={available ? 'success' : 'default'} label={available ? 'Available' : 'Unavailable'} />
              </Box>
              <Typography variant="body2" color="text.secondary">{getBackendStatus(backend)}</Typography>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>{getSafeDeviceLabel(backendName, backend?.devices ?? [])}</Typography>
            </Paper>
          );
        })}
      </Box>
    </Box>
  );
}
