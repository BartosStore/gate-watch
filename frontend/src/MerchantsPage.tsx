import { useEffect, useState } from 'react'
import FormControl from '@mui/material/FormControl'
import InputLabel from '@mui/material/InputLabel'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, formatDate, type MerchantItem } from './api'
import { gridLocale } from './gridLocale'
import { ErrorBox } from './QueryState'
import RunPanel from './RunPanel'
import StatusChip, { merchantStatus } from './StatusChip'

const STATUS_FILTERS = [
  { value: '', label: 'Všechny stavy' },
  { value: 'found', label: 'Nalezeno' },
  { value: 'timeout', label: 'Nedokončeno (timeout)' },
  { value: 'unreachable', label: 'Nedostupné' },
  { value: 'ok', label: 'OK' },
  { value: 'unchecked', label: 'Nezkontrolováno' },
]

const columns: GridColDef<MerchantItem>[] = [
  {
    field: 'id', headerName: 'Obchod', flex: 1, minWidth: 220,
    renderCell: ({ row }) => (
      <Stack sx={{ lineHeight: 1.3, justifyContent: 'center', height: '100%' }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>{row.id}</Typography>
        <Typography variant="caption" color="text.secondary">{row.url}</Typography>
      </Stack>
    ),
  },
  {
    field: 'last_outcome', headerName: 'Poslední kontrola', width: 170,
    renderCell: ({ row }) => <StatusChip status={merchantStatus(row.last_outcome, row.open_alerts)} title={row.last_error} />,
  },
  { field: 'last_error', headerName: 'Chyba', flex: 1, minWidth: 200, valueGetter: (v) => v ?? '' },
  {
    field: 'open_alerts', headerName: 'Nevyřízené nálezy', width: 150, align: 'center', headerAlign: 'center',
    renderCell: ({ row }) => (
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'center', height: '100%' }}>
        {row.open_alerts > 0 ? <StatusChip status="open" label={String(row.open_alerts)} /> : <span>0</span>}
        {(row.open_alerts > 0 || row.last_outcome === 'found') && (
          <Link to={`/nalezy?merchant_id=${encodeURIComponent(row.id)}`}>nálezy</Link>
        )}
      </Stack>
    ),
  },
  { field: 'last_check_at', headerName: 'Zkontrolováno', width: 170, valueFormatter: (v) => formatDate(v) },
  { field: 'last_duration_ms', headerName: 'Doba', width: 90, valueFormatter: (v: number | null) => (v == null ? '–' : `${(v / 1000).toFixed(1)} s`) },
]

export default function MerchantsPage() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 })

  useEffect(() => {
    const t = setTimeout(() => { setSearch(searchInput.trim()); setPagination((p) => ({ ...p, page: 0 })) }, 300)
    return () => clearTimeout(t)
  }, [searchInput])

  const q = useQuery({
    queryKey: ['merchants', search, status, pagination],
    queryFn: () => api.merchants({ search, status, page: pagination.page + 1, pageSize: pagination.pageSize }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <RunPanel />
      <Paper sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
          <TextField size="small" label="Hledat obchod (ID nebo URL)" value={searchInput} onChange={(e) => setSearchInput(e.target.value)} sx={{ width: 320 }} />
          <FormControl size="small" sx={{ width: 240 }}>
            <InputLabel>Stav poslední kontroly</InputLabel>
            <Select label="Stav poslední kontroly" value={status} onChange={(e) => { setStatus(e.target.value); setPagination((p) => ({ ...p, page: 0 })) }}>
              {STATUS_FILTERS.map((f) => <MenuItem key={f.value} value={f.value}>{f.label}</MenuItem>)}
            </Select>
          </FormControl>
        </Stack>
        {q.isError ? (
          <ErrorBox error={q.error} onRetry={() => q.refetch()} />
        ) : (
          <DataGrid
            rows={q.data?.items ?? []}
            columns={columns}
            rowCount={q.data?.total ?? 0}
            loading={q.isFetching && !q.data}
            paginationMode="server"
            paginationModel={pagination}
            onPaginationModelChange={setPagination}
            pageSizeOptions={[25, 50, 100]}
            disableColumnSorting
            disableColumnFilter
            disableRowSelectionOnClick
            rowHeight={56}
            autoHeight
            localeText={gridLocale(search || status ? 'Žádný obchod neodpovídá filtru.' : 'Zatím tu nejsou žádné obchody.')}
          />
        )}
      </Paper>
    </>
  )
}
