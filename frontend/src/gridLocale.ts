import { csCZ } from '@mui/x-data-grid/locales'

/** Czech DataGrid texts; a `localeText` prop replaces the theme ones, so it must include them all. */
export const gridLocale = (noRowsLabel: string) => ({
  ...csCZ.components.MuiDataGrid.defaultProps.localeText,
  noRowsLabel,
})
