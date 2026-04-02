import { useState } from 'react';
import {
  Box, Grid, Card, CardContent, Typography, Button,
  IconButton, Menu, MenuItem, ListItemIcon, ListItemText, Skeleton, Chip, Stack,
} from '@mui/material';
import { alpha } from '@mui/material/styles';
import AddIcon from '@mui/icons-material/Add';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import { useLocation } from 'wouter';
import TopBar from '../components/shell/TopBar';
import CreateFolderDialog from '../components/folders/CreateFolderDialog';
import RenameFolderDialog from '../components/folders/RenameFolderDialog';
import ConfirmDialog from '../components/common/ConfirmDialog';
import EmptyState from '../components/common/EmptyState';
import { useFolders } from '../hooks/useData';
import { foldersApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { formatBytes, formatDate } from '../utils/format';
import type { FolderSummaryDto } from '../types';
import FolderAppearanceAvatar from '../components/folders/FolderAppearanceAvatar';

export default function FoldersPage() {
  const { data: folders, isLoading } = useFolders();
  const [createOpen, setCreateOpen] = useState(false);
  const [renameTarget, setRenameTarget] = useState<FolderSummaryDto | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<FolderSummaryDto | null>(null);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [menuFolder, setMenuFolder] = useState<FolderSummaryDto | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [, navigate] = useLocation();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

  const handleMenuOpen = (e: React.MouseEvent<HTMLElement>, folder: FolderSummaryDto) => {
    e.stopPropagation();
    setMenuAnchor(e.currentTarget);
    setMenuFolder(folder);
  };

  const handleMenuClose = () => {
    setMenuAnchor(null);
    setMenuFolder(null);
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await foldersApi.remove(deleteTarget.id);
      await mutate('folders');
      notify('Folder deleted');
    } catch {
      notify('Failed to delete folder', 'error');
    } finally {
      setDeleting(false);
      setDeleteTarget(null);
    }
  };

  return (
    <>
      <TopBar
        title="Folders"
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
            Create Folder
          </Button>
        }
      />

      {isLoading ? (
        <Grid container spacing={2}>
          {[1, 2, 3].map((i) => (
            <Grid key={i} size={{ xs: 12, sm: 6, md: 4 }}>
              <Skeleton variant="rounded" height={120} />
            </Grid>
          ))}
        </Grid>
      ) : folders && folders.length > 0 ? (
        <Grid container spacing={2}>
          {folders.map((folder) => (
            <Grid key={folder.id} size={{ xs: 12, sm: 6, md: 4 }}>
              <Card
                sx={{
                  cursor: 'pointer',
                  borderRadius: 2,
                  bgcolor: 'background.paper',
                  '&:hover': {
                    borderColor: 'primary.main',
                    boxShadow: `0 4px 12px ${alpha('#1f5fbf', 0.12)}`,
                    transform: 'translateY(-2px)',
                  },
                }}
                onClick={() => navigate(`/folders/${folder.id}`)}
              >
                <CardContent sx={{ p: 2.5 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 1.5 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0, flexGrow: 1 }}>
                      <Box
                        sx={{ display: 'flex', alignItems: 'center' }}
                      >
                        <FolderAppearanceAvatar
                          iconKey={folder.iconKey}
                          colorHex={folder.colorHex}
                        />
                      </Box>
                      <Box sx={{ minWidth: 0 }}>
                        <Typography variant="h6" noWrap>{folder.name}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          Updated {formatDate(folder.updatedAtUtc)}
                        </Typography>
                      </Box>
                    </Box>
                    <IconButton
                      size="small"
                      onClick={(e) => handleMenuOpen(e, folder)}
                      aria-label="Folder actions"
                    >
                      <MoreVertIcon fontSize="small" />
                    </IconButton>
                  </Box>
                  <Stack direction="row" spacing={1} sx={{ mt: 1.5, flexWrap: 'wrap' }}>
                    <Chip
                      size="small"
                      label={`${folder.projectCount} project${folder.projectCount !== 1 ? 's' : ''}`}
                      sx={{ bgcolor: alpha('#1f5fbf', 0.08), color: 'primary.dark' }}
                    />
                    <Chip size="small" label={formatBytes(folder.totalSizeBytes)} variant="outlined" />
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>
      ) : (
        <EmptyState
          icon={<FolderOpenIcon />}
          title="No folders yet"
          description="Create your first folder to start organizing your recordings."
          action={
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
              Create Folder
            </Button>
          }
        />
      )}

      <Menu anchorEl={menuAnchor} open={!!menuAnchor} onClose={handleMenuClose}>
        <MenuItem onClick={() => { handleMenuClose(); if (menuFolder) navigate(`/folders/${menuFolder.id}`); }}>
          <ListItemIcon><UploadFileIcon fontSize="small" /></ListItemIcon>
          <ListItemText>Upload Files</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => { handleMenuClose(); if (menuFolder) setRenameTarget(menuFolder); }}>
          <ListItemIcon><EditIcon fontSize="small" /></ListItemIcon>
          <ListItemText>Edit</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => { handleMenuClose(); if (menuFolder) setDeleteTarget(menuFolder); }}>
          <ListItemIcon><DeleteIcon fontSize="small" color="error" /></ListItemIcon>
          <ListItemText sx={{ color: 'error.main' }}>Delete</ListItemText>
        </MenuItem>
      </Menu>

      <CreateFolderDialog open={createOpen} onClose={() => setCreateOpen(false)} />

      {renameTarget && (
        <RenameFolderDialog
          open
          folderId={renameTarget.id}
          currentName={renameTarget.name}
          currentIconKey={renameTarget.iconKey}
          currentColorHex={renameTarget.colorHex}
          onClose={() => setRenameTarget(null)}
        />
      )}

      <ConfirmDialog
        open={!!deleteTarget}
        title="Delete Folder"
        message={`Are you sure you want to delete "${deleteTarget?.name}"? This will delete all projects within it.`}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
        loading={deleting}
      />
    </>
  );
}
