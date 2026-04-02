import { alpha } from '@mui/material/styles';
import { Box, Paper, Typography, type SxProps } from '@mui/material';
import type { ReactNode } from 'react';

interface Props {
  icon?: ReactNode;
  title: string;
  description?: string;
  children?: ReactNode;
  action?: ReactNode;
  sx?: SxProps;
}

export default function EmptyState({ icon, title, description, children, action, sx }: Props) {
  return (
    <Paper
      variant="outlined"
      sx={{
        textAlign: 'center',
        py: 7,
        px: 3,
        borderRadius: 2,
        bgcolor: alpha('#ffffff', 0.92),
        ...sx,
      }}
    >
      {icon && <Box sx={{ mb: 2, color: 'text.secondary', '& .MuiSvgIcon-root': { fontSize: 48 } }}>{icon}</Box>}
      <Typography variant="h6" gutterBottom>{title}</Typography>
      {description && <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5, maxWidth: 420, mx: 'auto' }}>{description}</Typography>}
      {children}
      {action}
    </Paper>
  );
}
