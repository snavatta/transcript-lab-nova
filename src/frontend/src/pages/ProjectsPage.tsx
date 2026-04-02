import { useState } from 'react';
import {
  Box,
  InputAdornment,
  Paper,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import { useLocation } from 'wouter';
import TopBar from '../components/shell/TopBar';
import EmptyState from '../components/common/EmptyState';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import { useProjects } from '../hooks/useData';
import { formatBytes, formatDate, formatDuration } from '../utils/format';

export default function ProjectsPage() {
  const [search, setSearch] = useState('');
  const { data: projects, isLoading } = useProjects({ search: search || undefined });
  const [, navigate] = useLocation();

  return (
    <>
      <TopBar title="Projects" />

      <Box sx={{ mt: 3, mb: 2, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
        <TextField
          size="small"
          placeholder="Search projects..."
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ minWidth: 250 }}
        />
        <Typography variant="body2" color="text.secondary">
          {(projects ?? []).length} project{(projects?.length ?? 0) !== 1 ? 's' : ''}
        </Typography>
      </Box>

      {isLoading ? (
        <Skeleton variant="rounded" height={240} />
      ) : projects && projects.length > 0 ? (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Folder</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Duration</TableCell>
                <TableCell>Transcribed In</TableCell>
                <TableCell>Size</TableCell>
                <TableCell>Updated</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {projects.map((project) => (
                <TableRow
                  key={project.id}
                  hover
                  sx={{ cursor: 'pointer' }}
                  onClick={() => navigate(`/projects/${project.id}`)}
                >
                  <TableCell>
                    <Typography variant="body2" fontWeight={500}>
                      {project.name}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {project.originalFileName}
                    </Typography>
                  </TableCell>
                  <TableCell>{project.folderName}</TableCell>
                  <TableCell>
                    <ProjectStatusChip status={project.status} />
                  </TableCell>
                  <TableCell>{formatDuration(project.durationMs)}</TableCell>
                  <TableCell>{formatDuration(project.transcriptionElapsedMs)}</TableCell>
                  <TableCell>{formatBytes(project.totalSizeBytes)}</TableCell>
                  <TableCell>{formatDate(project.updatedAtUtc)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ) : (
        <EmptyState
          title="No projects found"
          description={search ? 'Try a different search term.' : 'Projects will appear here after uploads are created.'}
        />
      )}
    </>
  );
}
