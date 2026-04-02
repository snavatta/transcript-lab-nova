import { CssBaseline, ThemeProvider } from '@mui/material';
import { SWRConfig } from 'swr';
import { Route, Switch } from 'wouter';
import theme from './theme';
import AppShell from './components/shell/AppShell';
import { NotificationProvider } from './components/NotificationProvider';
import DashboardPage from './pages/DashboardPage';
import FoldersPage from './pages/FoldersPage';
import FolderDetailPage from './pages/FolderDetailPage';
import ProjectDetailPage from './pages/ProjectDetailPage';
import QueuePage from './pages/QueuePage';
import SettingsPage from './pages/SettingsPage';

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <SWRConfig value={{ revalidateOnFocus: true, dedupingInterval: 2000 }}>
        <NotificationProvider>
          <AppShell>
            <Switch>
              <Route path="/" component={DashboardPage} />
              <Route path="/folders" component={FoldersPage} />
              <Route path="/folders/:folderId" component={FolderDetailPage} />
              <Route path="/projects/:projectId" component={ProjectDetailPage} />
              <Route path="/queue" component={QueuePage} />
              <Route path="/settings" component={SettingsPage} />
            </Switch>
          </AppShell>
        </NotificationProvider>
      </SWRConfig>
    </ThemeProvider>
  );
}

export default App;
