import { useCallback, useState, useRef } from 'react';
import { Box, Typography, Button } from '@mui/material';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import { isAcceptedFile, ACCEPTED_EXTENSIONS } from '../../utils/format';

interface Props {
  onFilesSelected: (files: File[]) => void;
}

export default function UploadDropzone({ onFilesSelected }: Props) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const files = Array.from(e.dataTransfer.files).filter(isAcceptedFile);
    if (files.length > 0) onFilesSelected(files);
  }, [onFilesSelected]);

  const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []).filter(isAcceptedFile);
    if (files.length > 0) onFilesSelected(files);
    if (inputRef.current) inputRef.current.value = '';
  }, [onFilesSelected]);

  return (
    <Box
      onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
      onDragLeave={() => setDragOver(false)}
      onDrop={handleDrop}
      sx={{
        border: 2,
        borderStyle: 'dashed',
        borderColor: dragOver ? 'primary.main' : 'grey.300',
        borderRadius: 2,
        p: 4,
        textAlign: 'center',
        bgcolor: dragOver ? 'action.hover' : 'background.paper',
        cursor: 'pointer',
        transition: 'all 0.2s',
      }}
      onClick={() => inputRef.current?.click()}
    >
      <CloudUploadIcon sx={{ fontSize: 48, color: 'text.secondary', mb: 1 }} />
      <Typography variant="body1" gutterBottom>Drag and drop files here</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Supported: {ACCEPTED_EXTENSIONS.join(', ')}
      </Typography>
      <Button variant="outlined" component="span">Browse Files</Button>
      <input
        ref={inputRef}
        type="file"
        multiple
        hidden
        accept={ACCEPTED_EXTENSIONS.map((e) => `.${e}`).join(',')}
        onChange={handleChange}
      />
    </Box>
  );
}
