import { useContext } from 'react';
import { ShellLayoutContext } from './ShellLayoutContext';

export function useShellLayout() {
  const value = useContext(ShellLayoutContext);

  if (value == null) {
    throw new Error('useShellLayout must be used within a ShellLayoutProvider');
  }

  return value;
}
