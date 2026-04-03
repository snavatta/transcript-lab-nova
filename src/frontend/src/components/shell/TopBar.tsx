import { alpha } from '@mui/material/styles';
import {
  AppBar,
  Toolbar,
  Typography,
  Box,
  Breadcrumbs,
  Link as MuiLink,
  IconButton,
  Popover,
} from '@mui/material';
import MenuIcon from '@mui/icons-material/Menu';
import MoreHorizIcon from '@mui/icons-material/MoreHoriz';
import { useLocation } from 'wouter';
import { CONTENT_MAX_WIDTH, DESKTOP_TOP_BAR_HEIGHT, DRAWER_WIDTH, MOBILE_TOP_BAR_HEIGHT } from './layout';
import { useShellLayout } from './ShellLayoutContext';
import { Children, useMemo, useState, type ReactNode } from 'react';

interface Crumb {
  label: string;
  href?: string;
}

interface Props {
  title: ReactNode;
  breadcrumbs?: Crumb[];
  actions?: ReactNode;
}

function TopBarActions({ actions, mobile }: { actions?: ReactNode; mobile: boolean }) {
  const actionItems = useMemo(
    () => Children.toArray(actions).filter((node) => node != null),
    [actions],
  );
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);

  if (actionItems.length === 0) {
    return null;
  }

  if (!mobile || actionItems.length <= 2) {
    return (
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 1,
          flexShrink: 0,
          width: { xs: '100%', md: 'auto' },
        }}
      >
        {actionItems.map((action, index) => (
          <Box
            key={index}
            sx={{
              display: 'flex',
              flex: {
                xs: actionItems.length === 1 ? '1 1 100%' : '0 0 auto',
                md: '0 0 auto',
              },
              minWidth: 0,
            }}
          >
            {action}
          </Box>
        ))}
      </Box>
    );
  }

  return (
    <>
      <Box sx={{ display: 'flex', gap: 1, width: '100%' }}>
        <Box sx={{ display: 'flex', flex: '1 1 auto', minWidth: 0 }}>
          {actionItems[0]}
        </Box>
        <IconButton
          aria-label="More actions"
          color="inherit"
          onClick={(event) => setAnchorEl(event.currentTarget)}
        >
          <MoreHorizIcon />
        </IconButton>
      </Box>
      <Popover
        open={anchorEl != null}
        anchorEl={anchorEl}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
      >
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, p: 1.25, minWidth: 180 }}>
          {actionItems.slice(1).map((action, index) => (
            <Box key={index} sx={{ display: 'flex', minWidth: 0 }}>
              {action}
            </Box>
          ))}
        </Box>
      </Popover>
    </>
  );
}

export default function TopBar({ title, breadcrumbs, actions }: Props) {
  const [, navigate] = useLocation();
  const { isMobile, toggleMobileNav } = useShellLayout();

  return (
    <AppBar
      position="fixed"
      color="inherit"
      elevation={0}
      sx={{
        width: {
          xs: '100%',
          md: `calc(100% - ${DRAWER_WIDTH}px)`,
        },
        ml: {
          xs: 0,
          md: `${DRAWER_WIDTH}px`,
        },
        borderBottom: 1,
        borderColor: 'divider',
        backgroundColor: alpha('#ffffff', 0.94),
      }}
    >
      <Toolbar
        sx={{
          minHeight: {
            xs: MOBILE_TOP_BAR_HEIGHT,
            md: DESKTOP_TOP_BAR_HEIGHT,
          },
          px: 0,
          py: { xs: 1.25, md: 0 },
          alignItems: 'stretch',
        }}
      >
        <Box
          sx={{
            width: '100%',
            maxWidth: CONTENT_MAX_WIDTH,
            mx: 'auto',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            gap: 1,
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
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1.5, minWidth: 0 }}>
            {isMobile && (
              <IconButton
                aria-label="Open navigation"
                color="inherit"
                onClick={toggleMobileNav}
                sx={{ mt: 0.125 }}
              >
                <MenuIcon />
              </IconButton>
            )}
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            {breadcrumbs && breadcrumbs.length > 0 && (
              <Breadcrumbs
                sx={{
                  mb: 0.5,
                  overflow: 'hidden',
                  '& .MuiBreadcrumbs-ol': {
                    flexWrap: 'nowrap',
                  },
                }}
              >
                {breadcrumbs.map((c, i) =>
                  c.href ? (
                    <MuiLink
                      key={i}
                      component="button"
                      variant="body2"
                      underline="hover"
                      color="text.secondary"
                      onClick={() => navigate(c.href!)}
                      sx={{
                        fontWeight: 600,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {c.label}
                    </MuiLink>
                  ) : (
                    <Typography
                      key={i}
                      variant="body2"
                      color="text.secondary"
                      sx={{
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {c.label}
                    </Typography>
                  ),
                )}
              </Breadcrumbs>
            )}
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0 }}>
              {typeof title === 'string' ? (
                  <Typography variant={isMobile ? 'h6' : 'h5'} noWrap>{title}</Typography>
              ) : (
                title
              )}
              </Box>
            </Box>
            {!isMobile && <TopBarActions actions={actions} mobile={false} />}
          </Box>
          {isMobile && <TopBarActions actions={actions} mobile />}
        </Box>
      </Toolbar>
    </AppBar>
  );
}
