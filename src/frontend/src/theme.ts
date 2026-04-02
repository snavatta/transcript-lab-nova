import { alpha, createTheme, responsiveFontSizes } from '@mui/material/styles';

let theme = createTheme({
  palette: {
    mode: 'light',
    primary: {
      main: '#2b5fb8',
      light: '#6d95dd',
      dark: '#1f447f',
      contrastText: '#ffffff',
    },
    secondary: {
      main: '#3aa0c8',
      light: '#7dcbe4',
      dark: '#236d8f',
      contrastText: '#ffffff',
    },
    success: {
      main: '#2e7d32',
    },
    warning: {
      main: '#ed6c02',
    },
    error: {
      main: '#d32f2f',
    },
    info: {
      main: '#1f8fb8',
    },
    text: {
      primary: '#1c1f23',
      secondary: '#5f6368',
    },
    background: {
      default: '#f3f4f6',
      paper: '#ffffff',
    },
    divider: alpha('#1c1f23', 0.08),
  },
    typography: {
    fontFamily: '"Roboto", "Helvetica", "Arial", sans-serif',
    h3: {
      fontWeight: 500,
      letterSpacing: 0,
    },
    h4: {
      fontWeight: 500,
      letterSpacing: 0,
    },
    h5: {
      fontWeight: 500,
      letterSpacing: 0,
    },
    h6: {
      fontWeight: 500,
      letterSpacing: 0,
    },
    subtitle1: {
      fontWeight: 600,
    },
    subtitle2: {
      fontWeight: 600,
      letterSpacing: '0.03em',
    },
    button: {
      fontWeight: 500,
      letterSpacing: 0,
      textTransform: 'none',
    },
  },
  shape: {
    borderRadius: 6,
  },
  shadows: [
    'none',
    '0 10px 24px rgba(11, 31, 59, 0.05)',
    '0 12px 28px rgba(11, 31, 59, 0.06)',
    '0 16px 32px rgba(11, 31, 59, 0.08)',
    '0 18px 38px rgba(11, 31, 59, 0.10)',
    '0 20px 42px rgba(11, 31, 59, 0.12)',
    '0 24px 48px rgba(11, 31, 59, 0.14)',
    '0 28px 56px rgba(11, 31, 59, 0.16)',
    '0 32px 64px rgba(11, 31, 59, 0.18)',
    '0 36px 72px rgba(11, 31, 59, 0.20)',
    '0 40px 80px rgba(11, 31, 59, 0.22)',
    '0 44px 88px rgba(11, 31, 59, 0.24)',
    '0 48px 96px rgba(11, 31, 59, 0.26)',
    '0 52px 104px rgba(11, 31, 59, 0.28)',
    '0 56px 112px rgba(11, 31, 59, 0.30)',
    '0 60px 120px rgba(11, 31, 59, 0.32)',
    '0 64px 128px rgba(11, 31, 59, 0.34)',
    '0 68px 136px rgba(11, 31, 59, 0.36)',
    '0 72px 144px rgba(11, 31, 59, 0.38)',
    '0 76px 152px rgba(11, 31, 59, 0.40)',
    '0 80px 160px rgba(11, 31, 59, 0.42)',
    '0 84px 168px rgba(11, 31, 59, 0.44)',
    '0 88px 176px rgba(11, 31, 59, 0.46)',
    '0 92px 184px rgba(11, 31, 59, 0.48)',
    '0 96px 192px rgba(11, 31, 59, 0.50)',
  ],
  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          backgroundColor: '#f3f4f6',
        },
        '#root': {
          minHeight: '100vh',
        },
        '*': {
          boxSizing: 'border-box',
        },
      },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: {
          borderRadius: 7,
          paddingInline: 16,
        },
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: {
          backgroundImage: 'none',
        },
        outlined: {
          borderColor: alpha('#16365d', 0.08),
        },
      },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: {
        root: {
          borderColor: alpha('#1c1f23', 0.08),
          boxShadow: '0 1px 2px rgba(0, 0, 0, 0.04)',
          transition: 'transform 180ms ease, box-shadow 180ms ease, border-color 180ms ease',
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: {
          fontWeight: 500,
          borderRadius: 6,
        },
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: {
          borderRight: 'none',
        },
      },
    },
    MuiAppBar: {
      styleOverrides: {
        root: {
          backdropFilter: 'blur(18px)',
        },
      },
    },
    MuiTabs: {
      styleOverrides: {
        indicator: {
          height: 2,
          borderRadius: 2,
        },
      },
    },
    MuiTab: {
      styleOverrides: {
        root: {
          minHeight: 44,
        },
      },
    },
  },
});

theme = responsiveFontSizes(theme);

export default theme;
