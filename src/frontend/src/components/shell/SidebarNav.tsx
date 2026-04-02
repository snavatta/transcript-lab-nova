import { alpha } from '@mui/material/styles';
import {
  Box,
  Divider,
  Drawer,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Typography,
} from '@mui/material';
import DashboardIcon from '@mui/icons-material/Dashboard';
import FolderIcon from '@mui/icons-material/Folder';
import QueueIcon from '@mui/icons-material/Queue';
import SettingsIcon from '@mui/icons-material/Settings';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { useLocation, useRoute } from 'wouter';

const DRAWER_WIDTH = 256;

const navItems = [
  { label: 'Dashboard', icon: <DashboardIcon />, path: '/' },
  { label: 'Folders', icon: <FolderIcon />, path: '/folders' },
  { label: 'Queue', icon: <QueueIcon />, path: '/queue' },
  { label: 'Settings', icon: <SettingsIcon />, path: '/settings' },
];

export default function SidebarNav() {
  const [location, navigate] = useLocation();
  const [isFolder] = useRoute('/folders/:id');

  const isActive = (path: string) => {
    if (path === '/') return location === '/';
    if (path === '/folders') return location === '/folders' || isFolder;
    return location.startsWith(path);
  };

  return (
    <Drawer
      variant="permanent"
      sx={{
        width: DRAWER_WIDTH,
        flexShrink: 0,
        '& .MuiDrawer-paper': {
          width: DRAWER_WIDTH,
          boxSizing: 'border-box',
          backgroundColor: 'background.paper',
          borderRight: `1px solid ${alpha('#1c1f23', 0.08)}`,
          px: 1.5,
          py: 1,
        },
      }}
    >
      <Toolbar disableGutters sx={{ alignItems: 'flex-start', pt: 1.5, pb: 2 }}>
        <Box sx={{ width: '100%', display: 'flex', alignItems: 'flex-start', gap: 1.25 }}>
          <Box
            sx={{
              width: 36,
              height: 36,
              borderRadius: 1.5,
              display: 'grid',
              placeItems: 'center',
              border: `1px solid ${alpha('#3aa0c8', 0.24)}`,
              color: 'secondary.dark',
              bgcolor: alpha('#3aa0c8', 0.06),
              flexShrink: 0,
            }}
          >
            <AutoAwesomeIcon sx={{ fontSize: 18 }} />
          </Box>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: '0.1em' }}>
              Nova Workspace
            </Typography>
            <Typography variant="h6" noWrap>
              TranscriptLab Nova
            </Typography>
          </Box>
        </Box>
      </Toolbar>
      <Divider sx={{ mb: 1.5 }} />
      <List sx={{ px: 0.5 }}>
        {navItems.map((item) => (
          <ListItemButton
            key={item.path}
            selected={isActive(item.path)}
            onClick={() => navigate(item.path)}
            sx={{
              borderRadius: 2,
              mb: 0.75,
              minHeight: 48,
              '&.Mui-selected': {
                bgcolor: alpha('#1f5fbf', 0.1),
                color: 'primary.dark',
                '& .MuiListItemIcon-root': {
                  color: 'primary.main',
                },
                '&:hover': { bgcolor: alpha('#1f5fbf', 0.14) },
              },
              '&:hover': { bgcolor: 'action.hover' },
            }}
          >
            <ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}>{item.icon}</ListItemIcon>
            <ListItemText
              primary={item.label}
              secondary={item.label === 'Queue' ? 'Jobs' : item.label === 'Folders' ? 'Collections' : item.label === 'Settings' ? 'Defaults' : 'Overview'}
              slotProps={{
                primary: { fontWeight: 500, fontSize: 15 },
                secondary: { color: isActive(item.path) ? 'secondary.dark' : 'text.secondary', fontSize: 12 },
              }}
            />
          </ListItemButton>
        ))}
      </List>
    </Drawer>
  );
}

export { DRAWER_WIDTH };
