import { Box, Toolbar } from '@mui/material';
import SidebarNav from './SidebarNav';
import type { ReactNode } from 'react';

const CONTENT_MAX_WIDTH = 1280;

interface Props {
  children: ReactNode;
}

export default function AppShell({ children }: Props) {
  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <SidebarNav />
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          position: 'relative',
        }}
      >
        <Toolbar />
        <Box
          sx={{
            px: { xs: 2, md: 4 },
            py: 3,
            width: '100%',
            maxWidth: CONTENT_MAX_WIDTH,
            mx: 'auto',
          }}
        >
          {children}
        </Box>
      </Box>
    </Box>
  );
}

export { CONTENT_MAX_WIDTH };
