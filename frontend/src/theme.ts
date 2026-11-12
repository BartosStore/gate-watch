import { createTheme } from '@mui/material/styles'
import { csCZ } from '@mui/material/locale'
import { csCZ as gridCsCZ } from '@mui/x-data-grid/locales'
import type {} from '@mui/x-data-grid/themeAugmentation'

/** Status colors used everywhere (StatusChip, stat cards). Red/yellow/orange/green/grey as required. */
export const STATUS_COLORS = {
  red: { main: '#dc2626', soft: '#fef2f2', border: '#fecaca', text: '#b91c1c' },
  yellow: { main: '#eab308', soft: '#fefce8', border: '#fde68a', text: '#854d0e' },
  orange: { main: '#ea580c', soft: '#fff7ed', border: '#fed7aa', text: '#c2410c' },
  green: { main: '#16a34a', soft: '#f0fdf4', border: '#bbf7d0', text: '#15803d' },
  grey: { main: '#94a3b8', soft: '#f8fafc', border: '#e2e8f0', text: '#475569' },
} as const

const border = '#e5e7eb'

export const theme = createTheme(
  {
    palette: {
      primary: { main: '#4f46e5', dark: '#4338ca', light: '#818cf8', contrastText: '#fff' },
      background: { default: '#f5f6fa', paper: '#ffffff' },
      text: { primary: '#0f172a', secondary: '#64748b' },
      divider: border,
      error: { main: STATUS_COLORS.red.main },
      warning: { main: STATUS_COLORS.orange.main },
      success: { main: STATUS_COLORS.green.main },
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily: '"Inter", "Segoe UI Variable", "Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif',
      h4: { fontWeight: 700, letterSpacing: '-0.02em' },
      h5: { fontWeight: 700, letterSpacing: '-0.01em' },
      h6: { fontWeight: 650 },
      subtitle2: { fontWeight: 600 },
      button: { textTransform: 'none', fontWeight: 600 },
      overline: { fontWeight: 600, letterSpacing: '0.06em' },
    },
    components: {
      MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: 8 } } },
      MuiPaper: {
        defaultProps: { elevation: 0 },
        styleOverrides: { root: { backgroundImage: 'none' }, outlined: { borderColor: border } },
      },
      MuiCard: { defaultProps: { variant: 'outlined' } },
      MuiToggleButton: { styleOverrides: { root: { textTransform: 'none', fontWeight: 600, paddingInline: 14 } } },
      MuiTooltip: { styleOverrides: { tooltip: { fontSize: 12 } } },
      MuiDataGrid: {
        styleOverrides: {
          root: {
            border: 'none',
            '--DataGrid-containerBackground': '#f8fafc',
            '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 600, color: '#475569', fontSize: 13 },
            '& .MuiDataGrid-cell:focus, & .MuiDataGrid-cell:focus-within, & .MuiDataGrid-columnHeader:focus': { outline: 'none' },
            '& .MuiDataGrid-row:hover': { backgroundColor: '#f8fafc' },
          },
        },
      },
    },
  },
  csCZ,
  gridCsCZ,
)
