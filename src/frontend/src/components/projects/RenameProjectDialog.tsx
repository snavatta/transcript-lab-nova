import { useEffect, useState } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useSWRConfig } from 'swr';
import { projectsApi } from '../../api';
import { useNotification } from '../notifications';

interface Props {
  open: boolean;
  projectId: string;
  currentName: string;
  folderId: string;
  onClose: () => void;
}

export default function RenameProjectDialog({
  open,
  projectId,
  currentName,
  folderId,
  onClose,
}: Props) {
  const [name, setName] = useState(currentName);
  const [nameError, setNameError] = useState('');
  const [loading, setLoading] = useState(false);
  const { mutate } = useSWRConfig();
  const { notify } = useNotification();

  useEffect(() => {
    setName(currentName);
    setNameError('');
  }, [currentName]);

  const handleSubmit = async () => {
    const trimmed = name.trim();
    if (!trimmed || trimmed.length > 120) {
      setNameError('Name must be between 1 and 120 characters');
      return;
    }

    setLoading(true);
    try {
      await projectsApi.update(projectId, { name: trimmed });
      await mutate(`projects/${projectId}`);
      await mutate((key: unknown) => typeof key === 'string' && key.startsWith('projects?'), undefined);
      await mutate('queue');
      await mutate(`folders/${folderId}`);
      notify('Project updated');
      onClose();
    } catch {
      setNameError('Failed to update project');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Edit Project Name</DialogTitle>
      <DialogContent>
        <TextField
          autoFocus
          fullWidth
          margin="normal"
          label="Project Name"
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setNameError('');
          }}
          error={!!nameError}
          helperText={nameError || 'Shown in project lists, queue views, and exports.'}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={loading}>Cancel</Button>
        <Button onClick={handleSubmit} variant="contained" disabled={loading || !name.trim()}>Save</Button>
      </DialogActions>
    </Dialog>
  );
}
