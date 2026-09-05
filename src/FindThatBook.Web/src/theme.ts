import { createTheme } from '@mui/material/styles';

/**
 * A quiet, readable theme. The page is mostly long-form text -- titles, author names, and a
 * sentence of reasoning per result -- so type and spacing matter more than colour here.
 */
export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#1c5d99' },
    secondary: { main: '#6b4e9b' },
    background: { default: '#f6f7f9', paper: '#ffffff' },
  },
  shape: { borderRadius: 10 },
  typography: {
    fontFamily:
      '"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif',
    h1: { fontSize: '2rem', fontWeight: 600, letterSpacing: '-0.02em' },
    h2: { fontSize: '1.15rem', fontWeight: 600 },
    body2: { lineHeight: 1.6 },
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: { borderColor: 'rgba(0,0,0,0.08)' },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: { fontWeight: 500 },
      },
    },
  },
});
