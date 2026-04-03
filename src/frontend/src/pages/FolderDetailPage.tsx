import { useState, useCallback, useRef } from 'react';
import {
  Box, Typography, Button, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Paper, IconButton, Skeleton,
  TextField, InputAdornment,
  Chip,
  Stack,
} from '@mui/material';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import SearchIcon from '@mui/icons-material/Search';
import DeleteIcon from '@mui/icons-material/Delete';
import { useLocation, useRoute } from 'wouter';
import TopBar from '../components/shell/TopBar';
import UploadBatchDrawer from '../components/uploads/UploadBatchDrawer';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import EmptyState from '../components/common/EmptyState';
import ConfirmDialog from '../components/common/ConfirmDialog';
import { useFolder, useProjects } from '../hooks/useData';
import { projectsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { ACCEPTED_EXTENSIONS, formatBytes, formatDuration, formatDate, isAcceptedFile } from '../utils/format';
import type { ProjectSummaryDto } from '../types';
import FolderAppearanceAvatar from '../components/folders/FolderAppearanceAvatar';
import { useIsMobile } from '../hooks/useIsMobile';

export default function FolderDetailPage() {
  const isMobile = useIsMobile();
  const [, params] = useRoute('/folders/:folderId');
  const folderId = params?.folderId ?? '';
  const { data: folder, isLoading: folderLoading } = useFolder(folderId);
  const [search, setSearch] = useState('');
  const { data: projects, isLoading: projectsLoading } = useProjects({ folderId, search: search || undefined });
  const [uploadFiles, setUploadFiles] = useState<File[]>([]);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<ProjectSummaryDto | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [pageDragActive, setPageDragActive] = useState(false);
  const dragDepthRef = useRef(0);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [, navigate] = useLocation();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

  const handleFilesSelected = useCallback((files: File[]) => {
    setUploadFiles(files);
    setDrawerOpen(true);
  }, []);

  const openFilePicker = useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  const handleFileInputChange = useCallback((event: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []).filter(isAcceptedFile);
    if (files.length > 0) {
      handleFilesSelected(files);
    }
    event.target.value = '';
  }, [handleFilesSelected]);

  const hasDraggedFiles = (event: React.DragEvent) => Array.from(event.dataTransfer.types).includes('Files');

  const handlePageDragEnter = useCallback((event: React.DragEvent<HTMLDivElement>) => {
    if (!hasDraggedFiles(event)) return;
    event.preventDefault();
    dragDepthRef.current += 1;
    setPageDragActive(true);
  }, []);

  const handlePageDragOver = useCallback((event: React.DragEvent<HTMLDivElement>) => {
    if (!hasDraggedFiles(event)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    if (!pageDragActive) {
      setPageDragActive(true);
    }
  }, [pageDragActive]);

  const handlePageDragLeave = useCallback((event: React.DragEvent<HTMLDivElement>) => {
    if (!hasDraggedFiles(event)) return;
    event.preventDefault();
    dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
    if (dragDepthRef.current === 0) {
      setPageDragActive(false);
    }
  }, []);

  const handlePageDrop = useCallback((event: React.DragEvent<HTMLDivElement>) => {
    if (!hasDraggedFiles(event)) return;
    event.preventDefault();
    dragDepthRef.current = 0;
    setPageDragActive(false);
    const files = Array.from(event.dataTransfer.files).filter(isAcceptedFile);
    if (files.length > 0) {
      handleFilesSelected(files);
    }
  }, [handleFilesSelected]);

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await projectsApi.remove(deleteTarget.id);
      await mutate((key: unknown) => typeof key === 'string' && key.startsWith('projects'), undefined);
      await mutate(`folders/${folderId}`);
      await mutate('folders');
      notify('Project deleted');
    } catch {
      notify('Failed to delete project', 'error');
    } finally {
      setDeleting(false);
      setDeleteTarget(null);
    }
  };

  const isLoading = folderLoading || projectsLoading;

  return (
    <Box
      sx={{ position: 'relative', minHeight: 'calc(100vh - 160px)' }}
      onDragEnter={handlePageDragEnter}
      onDragOver={handlePageDragOver}
      onDragLeave={handlePageDragLeave}
      onDrop={handlePageDrop}
    >
      <TopBar
        title={(
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
            <FolderAppearanceAvatar
              iconKey={folder?.iconKey}
              colorHex={folder?.colorHex}
              size={32}
            />
            <Typography variant="h6" noWrap>
              {folder?.name ?? 'Folder'}
            </Typography>
          </Box>
        )}
        breadcrumbs={[
          { label: 'Folders', href: '/folders' },
          { label: folder?.name ?? '...' },
        ]}
        actions={
          <Button
            variant="contained"
            startIcon={<UploadFileIcon />}
            onClick={openFilePicker}
          >
            Upload Files
          </Button>
        }
      />
      <input
        ref={fileInputRef}
        type="file"
        multiple
        hidden
        accept={ACCEPTED_EXTENSIONS.map((extension) => `.${extension}`).join(',')}
        onChange={handleFileInputChange}
      />
      {pageDragActive && (
        <Box
          sx={{
            position: 'absolute',
            inset: 0,
            zIndex: 10,
            borderRadius: 2,
            border: '2px dashed',
            borderColor: 'primary.main',
            bgcolor: 'rgba(43, 95, 184, 0.08)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            pointerEvents: 'none',
          }}
        >
          <Paper
            variant="outlined"
            sx={{
              px: { xs: 2.5, sm: 4 },
              py: { xs: 2.5, sm: 3 },
              textAlign: 'center',
              bgcolor: 'background.paper',
              boxShadow: 3,
              maxWidth: 420,
              mx: 2,
            }}
          >
            <UploadFileIcon color="primary" sx={{ fontSize: 36, mb: 1 }} />
            <Typography variant="h6" gutterBottom>
              Drop files into {folder?.name ?? 'this folder'}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Release anywhere on the page to start the batch upload review.
            </Typography>
          </Paper>
        </Box>
      )}

      <Box sx={{ mt: 3, mb: 2, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
        <TextField
          size="small"
          placeholder="Search projects..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ width: { xs: '100%', sm: 'auto' }, minWidth: { sm: 250 } }}
        />
        {folder && (
          <Typography variant="body2" color="text.secondary" sx={{ width: { xs: '100%', sm: 'auto' } }}>
            {folder.projectCount} project{folder.projectCount !== 1 ? 's' : ''} &bull; {formatBytes(folder.totalSizeBytes)}
          </Typography>
        )}
      </Box>

      {isLoading ? (
        <Skeleton variant="rounded" height={200} />
      ) : projects && projects.length > 0 && isMobile ? (
        <Stack spacing={1.5}>
          {projects.map((project) => (
            <Paper
              key={project.id}
              variant="outlined"
              sx={{ p: 2, cursor: 'pointer' }}
              onClick={() => navigate(`/projects/${project.id}`)}
            >
              <Stack spacing={1.25}>
                <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography variant="subtitle2" noWrap>
                      {project.name}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {project.originalFileName}
                    </Typography>
                  </Box>
                  <IconButton
                    size="small"
                    onClick={(event) => {
                      event.stopPropagation();
                      setDeleteTarget(project);
                    }}
                    aria-label="Delete project"
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Box>

                <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
                  <ProjectStatusChip status={project.status} />
                  <Chip size="small" label={`Duration: ${formatDuration(project.durationMs)}`} variant="outlined" />
                  <Chip size="small" label={`Transcribed: ${formatDuration(project.transcriptionElapsedMs)}`} variant="outlined" />
                  <Chip size="small" label={`Size: ${formatBytes(project.totalSizeBytes)}`} variant="outlined" />
                </Stack>

                <Typography variant="caption" color="text.secondary">
                  Created {formatDate(project.createdAtUtc)}
                </Typography>
              </Stack>
            </Paper>
          ))}
        </Stack>
      ) : projects && projects.length > 0 ? (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Duration</TableCell>
                <TableCell>Transcribed In</TableCell>
                <TableCell>Size</TableCell>
                <TableCell>Created</TableCell>
                <TableCell width={48}></TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {projects.map((p) => (
                <TableRow
                  key={p.id}
                  hover
                  sx={{ cursor: 'pointer' }}
                  onClick={() => navigate(`/projects/${p.id}`)}
                >
                  <TableCell>
                    <Typography variant="body2" fontWeight={500}>{p.name}</Typography>
                    <Typography variant="caption" color="text.secondary">{p.originalFileName}</Typography>
                  </TableCell>
                  <TableCell>
                    <Box>
                      <ProjectStatusChip status={p.status} />
                    </Box>
                  </TableCell>
                  <TableCell>{formatDuration(p.durationMs)}</TableCell>
                  <TableCell>{formatDuration(p.transcriptionElapsedMs)}</TableCell>
                  <TableCell>{formatBytes(p.totalSizeBytes)}</TableCell>
                  <TableCell>{formatDate(p.createdAtUtc)}</TableCell>
                  <TableCell>
                    <IconButton
                      size="small"
                      onClick={(e) => { e.stopPropagation(); setDeleteTarget(p); }}
                      aria-label="Delete project"
                    >
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ) : (
        <EmptyState
          icon={<UploadFileIcon />}
          title="No projects yet"
          description="Upload files to create your first projects in this folder, or drag them anywhere onto this page."
        />
      )}

      <UploadBatchDrawer
        open={drawerOpen}
        folderId={folderId}
        files={uploadFiles}
        onClose={() => { setDrawerOpen(false); setUploadFiles([]); }}
      />

      <ConfirmDialog
        open={!!deleteTarget}
        title="Delete Project"
        message={`Delete "${deleteTarget?.name}"? This cannot be undone.`}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
        loading={deleting}
      />
    </Box>
  );
}
