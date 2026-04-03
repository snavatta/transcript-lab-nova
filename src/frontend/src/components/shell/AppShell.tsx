import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Box, Toolbar } from '@mui/material';
import SidebarNav from './SidebarNav';
import { ShellLayoutProvider } from './ShellLayoutContext';
import { CONTENT_MAX_WIDTH, DESKTOP_TOP_BAR_HEIGHT, MOBILE_TOP_BAR_HEIGHT } from './layout';
import { useIsMobile } from '../../hooks/useIsMobile';

interface Props {
  children: ReactNode;
}

export default function AppShell({ children }: Props) {
  const isMobile = useIsMobile();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  useEffect(() => {
    if (!isMobile) {
      setMobileNavOpen(false);
    }
  }, [isMobile]);

  const shellLayoutValue = useMemo(() => ({
    isMobile,
    mobileNavOpen,
    openMobileNav: () => setMobileNavOpen(true),
    closeMobileNav: () => setMobileNavOpen(false),
    toggleMobileNav: () => setMobileNavOpen((current) => !current),
  }), [isMobile, mobileNavOpen]);

  return (
    <ShellLayoutProvider value={shellLayoutValue}>
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
          <Toolbar
            sx={{
              minHeight: {
                xs: MOBILE_TOP_BAR_HEIGHT,
                md: DESKTOP_TOP_BAR_HEIGHT,
              },
            }}
          />
          <Box
            sx={{
              py: { xs: 2.5, md: 3 },
              pl: {
                xs: 'calc(16px + var(--safe-area-left))',
                sm: 'calc(24px + var(--safe-area-left))',
                md: 4,
              },
              pr: {
                xs: 'calc(16px + var(--safe-area-right))',
                sm: 'calc(24px + var(--safe-area-right))',
                md: 4,
              },
              pb: {
                xs: 'calc(24px + var(--safe-area-bottom))',
                md: 4,
              },
              width: '100%',
              maxWidth: CONTENT_MAX_WIDTH,
              mx: 'auto',
            }}
          >
            {children}
          </Box>
        </Box>
      </Box>
    </ShellLayoutProvider>
  );
}
