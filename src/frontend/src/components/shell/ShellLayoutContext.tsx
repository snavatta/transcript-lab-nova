import { createContext } from 'react';

export interface ShellLayoutContextValue {
  isMobile: boolean;
  mobileNavOpen: boolean;
  openMobileNav: () => void;
  closeMobileNav: () => void;
  toggleMobileNav: () => void;
}

export const ShellLayoutContext = createContext<ShellLayoutContextValue | null>(null);
