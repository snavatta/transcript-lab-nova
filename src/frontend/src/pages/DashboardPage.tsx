import { useState } from 'react';
import {
  Box,
  Grid,
  Card,
  CardContent,
  Typography,
  Button,
  Skeleton,
  Paper,
} from '@mui/material';
import { alpha } from '@mui/material/styles';
import AddIcon from '@mui/icons-material/Add';
import FolderIcon from '@mui/icons-material/Folder';
import QueueIcon from '@mui/icons-material/Queue';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorIcon from '@mui/icons-material/Error';
import { useLocation } from 'wouter';
import TopBar from '../components/shell/TopBar';
import CreateFolderDialog from '../components/folders/CreateFolderDialog';
import { useFolders, useQueue } from '../hooks/useData';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import { useIsMobile } from '../hooks/useIsMobile';

export default function DashboardPage() {
  const isMobile = useIsMobile();
  const { data: folders, isLoading: foldersLoading } = useFolders();
  const { data: queue, isLoading: queueLoading } = useQueue();
  const [createOpen, setCreateOpen] = useState(false);
  const [, navigate] = useLocation();

  const loading = foldersLoading || queueLoading;
  const queuedCount = queue?.queued.length ?? 0;
  const processingCount = queue?.processing.length ?? 0;
  const completedCount = queue?.completed.length ?? 0;
  const failedCount = queue?.failed.length ?? 0;
  const totalProjects = (folders ?? []).reduce((sum, folder) => sum + folder.projectCount, 0);

  const recentProjects = [
    ...(queue?.completed ?? []),
    ...(queue?.processing ?? []),
    ...(queue?.queued ?? []),
    ...(queue?.failed ?? []),
  ].slice(0, 8);

  const stats = [
    { label: 'Folders', value: folders?.length ?? 0, icon: <FolderIcon />, color: '#0b6efd', path: '/folders' },
    { label: 'Projects', value: totalProjects, icon: <QueueIcon />, color: '#0f7b6c', path: '/projects' },
    { label: 'Completed', value: completedCount, icon: <CheckCircleIcon />, color: '#1dbf73', path: '/queue' },
    { label: 'Failed', value: failedCount, icon: <ErrorIcon />, color: '#e65353', path: '/queue' },
  ];

  return (
    <>
      <TopBar
        title="Dashboard"
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
            Create Folder
          </Button>
        }
      />

      <Paper
        variant="outlined"
        sx={{
          mb: 3,
          p: { xs: 2.5, md: 4 },
          borderRadius: 2,
        }}
      >
        <Grid container spacing={3} alignItems="center">
          <Grid size={{ xs: 12, md: 8 }}>           
            <Typography variant="h4" sx={{ mb: 1 }}>
              TranscriptLab Nova
            </Typography>
            <Typography variant="body1" color="text.secondary" sx={{ maxWidth: 640, mb: 3 }}>
              Organize lectures by folder, queue uploads in batches, and move from processing to review with a clear, station-like workspace.
            </Typography>            
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <Box
              sx={{
                display: 'grid',
                gap: 1,
                borderTop: { xs: 2, md: 0 },
                borderLeft: { xs: 0, md: 2 },
                borderColor: alpha('#3aa0c8', 0.35),
                pl: { xs: 0, md: 2 },
                pt: { xs: 1.5, md: 0 },
              }}
            >
              <Typography variant="overline" color="secondary.dark">
                Live Console
              </Typography>
              <Typography variant="body2" color="text.secondary">Processing now: {processingCount}</Typography>
              <Typography variant="body2" color="text.secondary">Queued next: {queuedCount}</Typography>
              <Typography variant="body2" color="text.secondary">Recent completions: {completedCount}</Typography>
            </Box>
          </Grid>
        </Grid>
      </Paper>

      <Grid container spacing={2} sx={{ mb: 3 }}>
        {stats.map((stat) => (
          <Grid key={stat.label} size={{ xs: 6, md: 3 }}>
            <Card
              sx={{
                cursor: 'pointer',
                borderRadius: 2,
                bgcolor: 'background.paper',
                '&:hover': {
                  borderColor: stat.color,
                  boxShadow: `0 4px 12px ${alpha(stat.color, 0.12)}`,
                  transform: 'translateY(-2px)',
                },
              }}
              onClick={() => navigate(stat.path)}
            >
              <CardContent sx={{ p: 2.25 }}>
                {loading ? (
                  <Skeleton width={60} height={40} />
                ) : (
                  <>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 1.5 }}>
                      <Box
                        sx={{
                          width: 46,
                          height: 46,
                          display: 'grid',
                          placeItems: 'center',
                          borderRadius: 2,
                          bgcolor: alpha(stat.color, 0.1),
                          color: stat.color,
                          border: `1px solid ${alpha(stat.color, 0.12)}`,
                        }}
                      >
                        {stat.icon}
                      </Box>
                      <Typography variant={isMobile ? 'h5' : 'h4'}>{stat.value}</Typography>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <Typography variant="body2" color="text.secondary">{stat.label}</Typography>
                    </Box>
                  </>
                )}
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>

      {queuedCount + processingCount > 0 && (
        <Paper variant="outlined" sx={{ mb: 3, p: 2.5, borderRadius: 2 }}>
          <Typography variant="h6" gutterBottom>Active Jobs</Typography>
          <Typography variant="body2" color="text.secondary">
            {processingCount} processing, {queuedCount} queued.
          </Typography>
        </Paper>
      )}

      {recentProjects.length > 0 && (
        <Box>
          <Typography variant="h6" gutterBottom>Recent Projects</Typography>
          <Grid container spacing={1.5}>
            {recentProjects.map((p) => (
              <Grid key={p.id} size={{ xs: 12, sm: 6, md: 4 }}>
                <Card
                  sx={{
                    cursor: 'pointer',
                    borderRadius: 2,
                    bgcolor: 'background.paper',
                    '&:hover': { borderColor: 'primary.main', transform: 'translateY(-2px)' },
                  }}
                  onClick={() => navigate(`/projects/${p.id}`)}
                >
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <Typography variant="body2" noWrap sx={{ fontWeight: 500, flexGrow: 1, mr: 1 }}>
                        {p.name}
                      </Typography>
                      <ProjectStatusChip status={p.status} />
                    </Box>
                    <Typography variant="caption" color="text.secondary">{p.folderName}</Typography>
                  </CardContent>
                </Card>
              </Grid>
            ))}
          </Grid>
        </Box>
      )}

      <CreateFolderDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </>
  );
}
