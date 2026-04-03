import { alpha } from '@mui/material/styles';
import {
  Avatar,
  Box,
  Divider,
  Drawer,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  ListSubheader,
  Toolbar,
  Typography,
} from '@mui/material';
import DashboardIcon from '@mui/icons-material/Dashboard';
import FolderIcon from '@mui/icons-material/Folder';
import MonitorHeartIcon from '@mui/icons-material/MonitorHeart';
import QueueIcon from '@mui/icons-material/Queue';
import SettingsIcon from '@mui/icons-material/Settings';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { useLocation, useRoute } from 'wouter';
import { useShellLayout } from './ShellLayoutContext';
import { DRAWER_WIDTH } from './layout';

const navItems = [
  { label: 'Dashboard', icon: <DashboardIcon />, path: '/', group: 'primary' },
  { label: 'Folders', icon: <FolderIcon />, path: '/folders', group: 'primary' },
  { label: 'Queue', icon: <QueueIcon />, path: '/queue', group: 'primary' },
  { label: 'Diagnostics', icon: <MonitorHeartIcon />, path: '/diagnostics', group: 'utility' },
  { label: 'Settings', icon: <SettingsIcon />, path: '/settings', group: 'utility' },
];

export default function SidebarNav() {
  const [location, navigate] = useLocation();
  const [isFolder] = useRoute('/folders/:id');
  const { isMobile, mobileNavOpen, closeMobileNav } = useShellLayout();
  const primaryItems = navItems.filter((item) => item.group === 'primary');
  const utilityItems = navItems.filter((item) => item.group === 'utility');

  const isActive = (path: string) => {
    if (path === '/') return location === '/';
    if (path === '/folders') return location === '/folders' || isFolder;
    return location.startsWith(path);
  };

  const handleNavigate = (path: string) => {
    navigate(path);
    if (isMobile) {
      closeMobileNav();
    }
  };

  const drawerContent = (
    <>
      <Toolbar
        disableGutters
        sx={{
          alignItems: 'flex-start',
          pt: 1,
          pb: 1.5,
          minHeight: 'unset',
        }}
      >
        <Box sx={{ width: '100%', display: 'flex', alignItems: 'center', gap: 1.25, px: 0.5 }}>
          <Avatar
            sx={{
              width: 34,
              height: 34,
              borderRadius: 1.75,
              color: 'primary.dark',
              bgcolor: alpha('#2b5fb8', 0.06),
              border: `1px solid ${alpha('#16365d', 0.08)}`,
            }}
          >
            <AutoAwesomeIcon sx={{ fontSize: 17 }} />
          </Avatar>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1 }}>
              Nova
            </Typography>
            <Typography
              variant="subtitle1"
              noWrap
              sx={{ fontWeight: 700, letterSpacing: '-0.01em', color: 'text.primary' }}
            >
              TranscriptLab
            </Typography>
          </Box>
        </Box>
      </Toolbar>
      <Divider sx={{ mx: 0.5, mb: 1.25 }} />
      <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, flex: 1 }}>
        <List
          dense
          sx={{ px: 0.5 }}
          subheader={
            <ListSubheader
              disableGutters
              sx={{
                px: 1,
                py: 0.5,
                bgcolor: 'transparent',
                color: 'text.secondary',
                typography: 'overline',
                letterSpacing: '0.12em',
                lineHeight: 1.8,
              }}
            >
              Workspace
            </ListSubheader>
          }
        >
          {primaryItems.map((item) => {
            const active = isActive(item.path);

            return (
              <ListItemButton
                key={item.path}
                selected={active}
                onClick={() => handleNavigate(item.path)}
                sx={{
                  borderRadius: 1.5,
                  mb: 0.375,
                  minHeight: 44,
                  px: 1.25,
                  color: active ? 'text.primary' : 'text.secondary',
                  '&.Mui-selected': {
                    bgcolor: alpha('#2b5fb8', 0.08),
                    '& .MuiListItemIcon-root': {
                      color: 'primary.main',
                    },
                    '&:hover': {
                      bgcolor: alpha('#2b5fb8', 0.12),
                    },
                  },
                  '&:hover': {
                    bgcolor: alpha('#16365d', 0.04),
                  },
                }}
              >
                <ListItemIcon
                  sx={{
                    minWidth: 36,
                    color: active ? 'primary.main' : 'text.secondary',
                  }}
                >
                  {item.icon}
                </ListItemIcon>
                <ListItemText
                  primary={item.label}
                  primaryTypographyProps={{
                    fontWeight: active ? 600 : 500,
                    fontSize: 14,
                    letterSpacing: '-0.01em',
                  }}
                />
              </ListItemButton>
            );
          })}
        </List>

        <Box sx={{ flexGrow: 1 }} />

        <List
          dense
          sx={{ px: 0.5, pb: 0.75 }}
          subheader={
            <ListSubheader
              disableGutters
              sx={{
                px: 1,
                py: 0.5,
                bgcolor: 'transparent',
                color: 'text.secondary',
                typography: 'overline',
                letterSpacing: '0.12em',
                lineHeight: 1.8,
              }}
            >
              System
            </ListSubheader>
          }
        >
          {utilityItems.map((item) => {
            const active = isActive(item.path);

            return (
              <ListItemButton
                key={item.path}
                selected={active}
                onClick={() => handleNavigate(item.path)}
                sx={{
                  borderRadius: 1.5,
                  minHeight: 40,
                  px: 1.25,
                  color: active ? 'text.primary' : 'text.secondary',
                  '& .MuiListItemIcon-root': {
                    minWidth: 36,
                    color: active ? 'primary.main' : 'text.secondary',
                  },
                  '&.Mui-selected': {
                    bgcolor: alpha('#2b5fb8', 0.06),
                  },
                  '&:hover': {
                    bgcolor: alpha('#16365d', 0.04),
                  },
                }}
              >
                <ListItemIcon>{item.icon}</ListItemIcon>
                <ListItemText
                  primary={item.label}
                  primaryTypographyProps={{
                    fontWeight: active ? 600 : 500,
                    fontSize: 13.5,
                  }}
                />
              </ListItemButton>
            );
          })}
        </List>
      </Box>
    </>
  );

  return isMobile ? (
    <Drawer
      open={mobileNavOpen}
      onClose={closeMobileNav}
      variant="temporary"
      ModalProps={{ keepMounted: true }}
      sx={{
        display: { xs: 'block', md: 'none' },
        '& .MuiDrawer-paper': {
          width: 'min(88vw, 320px)',
          boxSizing: 'border-box',
          background: `linear-gradient(180deg, ${alpha('#ffffff', 0.98)} 0%, ${alpha('#f7f8fb', 0.98)} 100%)`,
          borderRight: `1px solid ${alpha('#16365d', 0.08)}`,
          pl: 'calc(10px + var(--safe-area-left))',
          pr: 1.25,
          pt: 1.25,
          pb: 'calc(10px + var(--safe-area-bottom))',
        },
      }}
    >
      {drawerContent}
    </Drawer>
  ) : (
    <Drawer
      variant="permanent"
      sx={{
        display: { xs: 'none', md: 'block' },
        width: DRAWER_WIDTH,
        flexShrink: 0,
        '& .MuiDrawer-paper': {
          width: DRAWER_WIDTH,
          boxSizing: 'border-box',
          background: `linear-gradient(180deg, ${alpha('#ffffff', 0.98)} 0%, ${alpha('#f7f8fb', 0.98)} 100%)`,
          borderRight: `1px solid ${alpha('#16365d', 0.08)}`,
          px: 1.25,
          py: 1.25,
        },
      }}
    >
      {drawerContent}
    </Drawer>
  );
}
