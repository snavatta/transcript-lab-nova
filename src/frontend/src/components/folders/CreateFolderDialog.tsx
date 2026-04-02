import { useState } from 'react';
import { Dialog, DialogTitle, DialogContent, DialogActions, Button } from '@mui/material';
import { foldersApi } from '../../api';
import { useNotification } from '../notifications';
import { useSWRConfig } from 'swr';
import FolderEditorFields from './FolderEditorFields';
import {
  DEFAULT_FOLDER_COLOR_HEX,
  DEFAULT_FOLDER_ICON_KEY,
  type FolderIconKey,
  isValidFolderColorHex,
  normalizeFolderColorHex,
} from '../../folders/appearance';

interface Props {
  open: boolean;
  onClose: () => void;
}

export default function CreateFolderDialog({ open, onClose }: Props) {
  const [name, setName] = useState('');
  const [iconKey, setIconKey] = useState(DEFAULT_FOLDER_ICON_KEY);
  const [colorHex, setColorHex] = useState(DEFAULT_FOLDER_COLOR_HEX);
  const [loading, setLoading] = useState(false);
  const [nameError, setNameError] = useState('');
  const [colorError, setColorError] = useState('');
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

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
      await foldersApi.create({
        name: trimmed,
        iconKey,
        colorHex: normalizeFolderColorHex(colorHex),
      });
      await mutate('folders');
      notify('Folder created');
      setName('');
      setIconKey(DEFAULT_FOLDER_ICON_KEY);
      setColorHex(DEFAULT_FOLDER_COLOR_HEX);
      setNameError('');
      setColorError('');
      onClose();
    } catch {
      setNameError('Failed to create folder');
    } finally {
      setLoading(false);
    }
  };

  const handleClose = () => {
    setName('');
    setIconKey(DEFAULT_FOLDER_ICON_KEY);
    setColorHex(DEFAULT_FOLDER_COLOR_HEX);
    setNameError('');
    setColorError('');
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth>
      <DialogTitle>Create Folder</DialogTitle>
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
        <Button onClick={handleClose} disabled={loading}>Cancel</Button>
        <Button onClick={handleSubmit} variant="contained" disabled={loading || !name.trim()}>Create</Button>
      </DialogActions>
    </Dialog>
  );
}
