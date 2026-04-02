import { useState } from 'react';
import { Button, Menu, MenuItem, ListItemIcon, ListItemText } from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import DescriptionIcon from '@mui/icons-material/Description';
import CodeIcon from '@mui/icons-material/Code';
import PictureAsPdfIcon from '@mui/icons-material/PictureAsPdf';
import TextSnippetIcon from '@mui/icons-material/TextSnippet';
import { projectsApi } from '../../api';

const formats = [
  { value: 'txt', label: 'Plain Text (.txt)', icon: <TextSnippetIcon fontSize="small" /> },
  { value: 'md', label: 'Markdown (.md)', icon: <DescriptionIcon fontSize="small" /> },
  { value: 'html', label: 'HTML (.html)', icon: <CodeIcon fontSize="small" /> },
  { value: 'pdf', label: 'PDF (.pdf)', icon: <PictureAsPdfIcon fontSize="small" /> },
];

interface Props {
  projectId: string;
  availableExports: string[];
}

export default function ExportMenu({ projectId, availableExports }: Props) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);

  const handleExport = (format: string) => {
    setAnchorEl(null);
    const url = projectsApi.exportUrl(projectId, format);
    window.open(url, '_blank');
  };

  return (
    <>
      <Button
        variant="outlined"
        startIcon={<DownloadIcon />}
        onClick={(e) => setAnchorEl(e.currentTarget)}
        disabled={availableExports.length === 0}
      >
        Export
      </Button>
      <Menu anchorEl={anchorEl} open={!!anchorEl} onClose={() => setAnchorEl(null)}>
        {formats
          .filter((f) => availableExports.includes(f.value))
          .map((f) => (
            <MenuItem key={f.value} onClick={() => handleExport(f.value)}>
              <ListItemIcon>{f.icon}</ListItemIcon>
              <ListItemText>{f.label}</ListItemText>
            </MenuItem>
          ))}
      </Menu>
    </>
  );
}
