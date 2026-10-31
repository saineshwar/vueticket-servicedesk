import { createTheme } from '@mui/material/styles'

// System fonts only: self-hosted installs may have no internet access.
const fontStack = '"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif'

// Tinted ("soft") chip colors: readable text on a light wash instead of saturated blocks.
const softChipColors = ['primary', 'secondary', 'success', 'warning', 'error', 'info'] as const

export const theme = createTheme({
  cssVariables: true,
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#1F4FB4' },
        secondary: { main: '#0E7C6B' },
        // A white app: structure comes from hairline borders and a faint grey for headers and hover.
        background: { default: '#FFFFFF', paper: '#FFFFFF' },
        text: { primary: '#16202C', secondary: '#5B6676' },
        divider: '#E4E8EE',
        action: { hover: 'rgba(31, 79, 180, 0.04)', selected: 'rgba(31, 79, 180, 0.08)' },
      },
    },
    dark: {
      palette: {
        primary: { main: '#86A6F2' },
        secondary: { main: '#5CC6B3' },
        background: { default: '#10151C', paper: '#171F29' },
        text: { primary: '#E5EAF1', secondary: '#9AA6B6' },
        divider: '#2A3442',
      },
    },
  },
  typography: {
    fontFamily: fontStack,
    h1: { fontSize: '1.625rem', fontWeight: 650, letterSpacing: '-0.015em' },
    h2: { fontSize: '1.2rem', fontWeight: 650, letterSpacing: '-0.005em' },
    h3: { fontSize: '1.05rem', fontWeight: 650 },
    button: { textTransform: 'none', fontWeight: 600 },
  },
  shape: { borderRadius: 8 },
  components: {
    MuiButton: { defaultProps: { disableElevation: true } },
    MuiPaper: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: { backgroundImage: 'none' },
        outlined: ({ theme }) => ({ borderRadius: 12, borderColor: theme.vars.palette.divider }),
      },
    },
    MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
    MuiAppBar: { defaultProps: { elevation: 0 } },
    MuiDrawer: {
      styleOverrides: { paper: ({ theme }) => ({ borderRightColor: theme.vars.palette.divider }) },
    },

    // ---- Grids ----
    MuiTableCell: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderBottomColor: theme.vars.palette.divider,
          paddingTop: 12,
          paddingBottom: 12,
          paddingLeft: 16,
          paddingRight: 16,
        }),
        sizeSmall: { paddingTop: 10, paddingBottom: 10 },
        head: ({ theme }) => ({
          backgroundColor: '#F8FAFC',
          color: theme.vars.palette.text.secondary,
          fontSize: '0.75rem',
          fontWeight: 600,
          letterSpacing: '0.04em',
          textTransform: 'uppercase',
          whiteSpace: 'nowrap',
          paddingTop: 10,
          paddingBottom: 10,
          ...theme.applyStyles('dark', { backgroundColor: '#1B2430' }),
        }),
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: ({ theme }) => ({
          '&:last-child > td': { borderBottom: 0 },
          '&.MuiTableRow-hover:hover': { backgroundColor: theme.vars.palette.action.hover },
        }),
      },
    },
    MuiTablePagination: {
      styleOverrides: {
        root: ({ theme }) => ({ borderTop: `1px solid ${theme.vars.palette.divider}` }),
        toolbar: { minHeight: 52 },
        selectLabel: ({ theme }) => ({ color: theme.vars.palette.text.secondary }),
        displayedRows: ({ theme }) => ({ color: theme.vars.palette.text.secondary }),
      },
    },

    // ---- Chips, tabs, alerts ----
    MuiChip: {
      styleOverrides: {
        root: { fontWeight: 600, borderRadius: 6 },
        sizeSmall: { height: 22, fontSize: '0.75rem', '& .MuiChip-label': { paddingLeft: 8, paddingRight: 8 } },
      },
      variants: softChipColors.map((color) => ({
        props: { variant: 'filled' as const, color },
        style: ({ theme }) => ({
          backgroundColor: `rgba(${theme.vars.palette[color].mainChannel} / 0.12)`,
          color: theme.vars.palette[color].dark,
          ...theme.applyStyles('dark', { color: theme.vars.palette[color].light }),
        }),
      })),
    },
    MuiTabs: {
      styleOverrides: {
        root: ({ theme }) => ({ minHeight: 44, borderBottom: `1px solid ${theme.vars.palette.divider}` }),
        indicator: { height: 3, borderTopLeftRadius: 3, borderTopRightRadius: 3 },
      },
    },
    MuiTab: { styleOverrides: { root: { minHeight: 44, fontWeight: 600, textTransform: 'none' } } },
    MuiAlert: { styleOverrides: { root: { borderRadius: 10 } } },
    MuiDialog: { styleOverrides: { paper: { borderRadius: 14 } } },
  },
})
