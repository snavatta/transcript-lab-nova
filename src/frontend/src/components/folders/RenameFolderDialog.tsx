import { useState, useEffect } from 'react';
import { Dialog, DialogTitle, DialogContent, DialogActions, Button } from '@mui/material';
import { foldersApi } from '../../api';
import { useNotification } from '../notifications';
import { useSWRConfig } from 'swr';
import FolderEditorFields from './FolderEditorFields';
import { type FolderIconKey, isValidFolderColorHex, normalizeFolderColorHex } from '../../folders/appearance';

interface Props {
  open: boolean;
  folderId: string;
  currentName: string;
  currentIconKey: string;
  currentColorHex: string;
  onClose: () => void;
}

export default function RenameFolderDialog({
  open,
  folderId,
  currentName,
  currentIconKey,
  currentColorHex,
  onClose,
}: Props) {
  const [name, setName] = useState(currentName);
  const [iconKey, setIconKey] = useState(currentIconKey);
  const [colorHex, setColorHex] = useState(currentColorHex);
  const [loading, setLoading] = useState(false);
  const [nameError, setNameError] = useState('');
  const [colorError, setColorError] = useState('');
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

  useEffect(() => {
    setName(currentName);
    setIconKey(currentIconKey);
    setColorHex(currentColorHex);
  }, [currentColorHex, currentIconKey, currentName]);

  const handleSubmit = async () => {
    const trimmed = name.trim();
    if (!trimmed || trimmed.length > 120) {
      setNameError('Name must be between 1 and 120 characters');
      return;
    }

    if (!isValidFolderColorHex(colorHex)) {
      setColorError('Color must use #RRGGBB format');
      return;
    }

    setLoading(true);
    try {
      await foldersApi.update(folderId, {
        name: trimmed,
        iconKey,
        colorHex: normalizeFolderColorHex(colorHex),
      });
      await mutate('folders');
      await mutate(`folders/${folderId}`);
      notify('Folder updated');
      onClose();
    } catch {
      setNameError('Failed to update folder');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Edit Folder</DialogTitle>
      <DialogContent>
        <FolderEditorFields
          name={name}
          iconKey={iconKey}
          colorHex={colorHex}
          nameError={nameError}
          colorError={colorError}
          onNameChange={(value) => {
            setName(value);
            setNameError('');
          }}
          onIconKeyChange={(value) => setIconKey(value as FolderIconKey)}
          onColorHexChange={(value) => {
            setColorHex(value.toUpperCase());
            setColorError('');
          }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={loading}>Cancel</Button>
        <Button onClick={handleSubmit} variant="contained" disabled={loading || !name.trim()}>Save</Button>
      </DialogActions>
    </Dialog>
  );
}
