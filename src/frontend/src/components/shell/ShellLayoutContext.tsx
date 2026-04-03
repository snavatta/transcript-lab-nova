import { createContext, useContext, type ReactNode } from 'react';

interface ShellLayoutContextValue {
  isMobile: boolean;
  mobileNavOpen: boolean;
  openMobileNav: () => void;
  closeMobileNav: () => void;
  toggleMobileNav: () => void;
}

const ShellLayoutContext = createContext<ShellLayoutContextValue | null>(null);

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

export function useShellLayout() {
  const value = useContext(ShellLayoutContext);

  if (value == null) {
    throw new Error('useShellLayout must be used within a ShellLayoutProvider');
  }

  return value;
}
