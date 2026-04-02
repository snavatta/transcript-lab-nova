import { alpha } from '@mui/material/styles';
import { AppBar, Toolbar, Typography, Box, Breadcrumbs, Link as MuiLink } from '@mui/material';
import { useLocation } from 'wouter';
import { CONTENT_MAX_WIDTH } from './AppShell';
import { DRAWER_WIDTH } from './SidebarNav';
import type { ReactNode } from 'react';

interface Crumb {
  label: string;
  href?: string;
}

interface Props {
  title: ReactNode;
  breadcrumbs?: Crumb[];
  actions?: ReactNode;
}

export default function TopBar({ title, breadcrumbs, actions }: Props) {
  const [, navigate] = useLocation();

  return (
    <AppBar
      position="fixed"
      color="inherit"
      elevation={0}
      sx={{
        width: `calc(100% - ${DRAWER_WIDTH}px)`,
        ml: `${DRAWER_WIDTH}px`,
        borderBottom: 1,
        borderColor: 'divider',
        backgroundColor: alpha('#ffffff', 0.94),
      }}
    >
      <Toolbar sx={{ height: 72, px: { xs: 2, md: 4 } }}>
        <Box
          sx={{
            width: '100%',
            maxWidth: CONTENT_MAX_WIDTH,
            mx: 'auto',
            display: 'flex',
            alignItems: 'center',
            gap: 2,
          }}
        >
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            {breadcrumbs && breadcrumbs.length > 0 && (
              <Breadcrumbs sx={{ mb: 0.5 }}>
                {breadcrumbs.map((c, i) =>
                  c.href ? (
                    <MuiLink
                      key={i}
                      component="button"
                      variant="body2"
                      underline="hover"
                      color="text.secondary"
                      onClick={() => navigate(c.href!)}
                      sx={{ fontWeight: 600 }}
                    >
                      {c.label}
                    </MuiLink>
                  ) : (
                    <Typography key={i} variant="body2" color="text.secondary">
                      {c.label}
                    </Typography>
                  ),
                )}
              </Breadcrumbs>
            )}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              {typeof title === 'string' ? (
                <Typography variant="h5" noWrap>{title}</Typography>
              ) : (
                title
              )}
            </Box>
          </Box>
          {actions && <Box sx={{ display: 'flex', gap: 1, flexShrink: 0 }}>{actions}</Box>}
        </Box>
      </Toolbar>
    </AppBar>
  );
}
