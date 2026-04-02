import { useState, useCallback, type ReactNode } from 'react';
import { Snackbar, Alert, type AlertColor } from '@mui/material';
import { NotificationContext } from './notifications';

export function NotificationProvider({ children }: { children: ReactNode }) {
  interface Notification {
    message: string;
    severity: AlertColor;
  }

  const [open, setOpen] = useState(false);
  const [current, setCurrent] = useState<Notification>({ message: '', severity: 'success' });

  const notify = useCallback((message: string, severity: AlertColor = 'success') => {
    setCurrent({ message, severity });
    setOpen(true);
  }, []);

  const handleClose = useCallback((_?: React.SyntheticEvent | Event, reason?: string) => {
    if (reason === 'clickaway') return;
    setOpen(false);
  }, []);

  return (
    <NotificationContext.Provider value={{ notify }}>
      {children}
      <Snackbar open={open} autoHideDuration={4000} onClose={handleClose} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        <Alert onClose={handleClose} severity={current.severity} variant="filled" sx={{ width: '100%' }}>
          {current.message}
        </Alert>
      </Snackbar>
    </NotificationContext.Provider>
  );
}
