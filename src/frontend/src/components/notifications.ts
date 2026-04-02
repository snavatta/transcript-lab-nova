import { createContext, useContext } from 'react';
import type { AlertColor } from '@mui/material';

export interface NotificationContextValue {
  notify: (message: string, severity?: AlertColor) => void;
}

export const NotificationContext = createContext<NotificationContextValue>({
  notify: () => {},
});

export function useNotification() {
  return useContext(NotificationContext);
}
