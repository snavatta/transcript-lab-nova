import type { ReactNode } from 'react';
import {
  Alert,
  Box,
  Chip,
  Paper,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import MemoryIcon from '@mui/icons-material/Memory';
import SpeedIcon from '@mui/icons-material/Speed';
import StorageIcon from '@mui/icons-material/Storage';
import ConstructionIcon from '@mui/icons-material/Construction';
import TopBar from '../components/shell/TopBar';
import { useDiagnostics } from '../hooks/useData';
import { formatBytes, formatDate, formatDuration } from '../utils/format';
import { formatEngineLabel } from '../utils/transcription';

function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`;
}

function MetricCard({
  title,
  value,
  helper,
  icon,
}: {
  title: string;
  value: string;
  helper: string;
  icon: ReactNode;
}) {
  return (
    <Paper variant="outlined" sx={{ p: 2.5, minHeight: 164 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Typography variant="subtitle2" color="text.secondary">
          {title}
        </Typography>
        <Box sx={{ color: 'secondary.main' }}>{icon}</Box>
      </Box>
      <Typography variant="h4" sx={{ mb: 1, fontWeight: 700 }}>
        {value}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {helper}
      </Typography>
    </Paper>
  );
}

export default function DiagnosticsPage() {
  const { data, isLoading } = useDiagnostics();

  if (isLoading || !data) {
    return (
      <>
        <TopBar title="Diagnostics" />
        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: 'repeat(2, minmax(0, 1fr))' } }}>
          <Skeleton variant="rounded" height={164} />
          <Skeleton variant="rounded" height={164} />
          <Skeleton variant="rounded" height={164} />
          <Skeleton variant="rounded" height={164} />
        </Box>
      </>
    );
  }

  const availableEngines = data.engines.filter((engine) => engine.isAvailable);
  const unavailableEngines = data.engines.filter((engine) => !engine.isAvailable);

  return (
    <>
      <TopBar title="Diagnostics" />

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        <Paper variant="outlined" sx={{ p: 2.5 }}>
          <Typography variant="h6" sx={{ mb: 0.5 }}>
            Runtime
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Sampled {formatDate(data.runtime.collectedAtUtc)}. CPU is the app process share across {data.runtime.processorCount} logical cores.
          </Typography>

          <Box
            sx={{
              mt: 2.5,
              display: 'grid',
              gap: 2,
              gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', xl: 'repeat(4, minmax(0, 1fr))' },
            }}
          >
            <MetricCard
              title="CPU Usage"
              value={formatPercent(data.runtime.cpuUsagePercent)}
              helper={`PID ${data.runtime.processId} • Uptime ${formatDuration(data.runtime.uptimeMs)}`}
              icon={<SpeedIcon />}
            />
            <MetricCard
              title="Working Set"
              value={formatBytes(data.runtime.workingSetBytes)}
              helper="Resident memory currently held by the API process."
              icon={<MemoryIcon />}
            />
            <MetricCard
              title="Private Memory"
              value={formatBytes(data.runtime.privateMemoryBytes)}
              helper="Private process memory reserved by the runtime."
              icon={<StorageIcon />}
            />
            <MetricCard
              title="Managed Heap"
              value={formatBytes(data.runtime.managedHeapBytes)}
              helper="Managed allocations tracked by the .NET GC."
              icon={<ConstructionIcon />}
            />
          </Box>
        </Paper>

        <Paper variant="outlined" sx={{ p: 2.5 }}>
          <Typography variant="h6" sx={{ mb: 0.5 }}>
            Engines
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Runtime engine availability reflects the current container or host environment.
          </Typography>

          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mb: availableEngines.length > 0 ? 2 : 0 }}>
            {availableEngines.map((engine) => (
              <Chip
                key={engine.engine}
                color="success"
                variant="outlined"
                label={`${formatEngineLabel(engine.engine)} • ${engine.models.join(', ')}`}
              />
            ))}
          </Box>

          {unavailableEngines.length > 0 && (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
              {unavailableEngines.map((engine) => (
                <Alert key={engine.engine} severity="warning" variant="outlined">
                  <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
                    {formatEngineLabel(engine.engine)}
                  </Typography>
                  <Typography variant="body2">
                    {engine.availabilityError ?? 'Unavailable for the current runtime.'}
                  </Typography>
                </Alert>
              ))}
            </Box>
          )}
        </Paper>

        <Paper variant="outlined" sx={{ p: 2.5 }}>
          <Typography variant="h6" sx={{ mb: 0.5 }}>
            Project Storage
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Projects are sorted by current total disk usage, largest first.
          </Typography>

          {data.projects.length === 0 ? (
            <Alert severity="info" variant="outlined">
              No projects yet.
            </Alert>
          ) : (
            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Project</TableCell>
                    <TableCell>Folder</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell>Original</TableCell>
                    <TableCell>Workspace</TableCell>
                    <TableCell>Total</TableCell>
                    <TableCell>Updated</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {data.projects.map((project) => (
                    <TableRow key={project.projectId} hover>
                      <TableCell>
                        <Typography variant="body2" fontWeight={600}>
                          {project.projectName}
                        </Typography>
                      </TableCell>
                      <TableCell>{project.folderName}</TableCell>
                      <TableCell>{project.status}</TableCell>
                      <TableCell>{formatBytes(project.originalFileSizeBytes)}</TableCell>
                      <TableCell>{formatBytes(project.workspaceSizeBytes)}</TableCell>
                      <TableCell>{formatBytes(project.totalSizeBytes)}</TableCell>
                      <TableCell>{formatDate(project.updatedAtUtc)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </Paper>
      </Box>
    </>
  );
}
