import type { ReactNode } from 'react';
import { ShellLayoutContext, type ShellLayoutContextValue } from './ShellLayoutContext';

interface ProviderProps {
  value: ShellLayoutContextValue;
  children: ReactNode;
}

export function ShellLayoutProvider({ value, children }: ProviderProps) {
  return (
    <ShellLayoutContext.Provider value={value}>
      {children}
    </ShellLayoutContext.Provider>
  );
}
