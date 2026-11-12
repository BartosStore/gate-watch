import { useEffect, useState } from 'react'
import Box from '@mui/material/Box'
import FormControl from '@mui/material/FormControl'
import InputAdornment from '@mui/material/InputAdornment'
import InputLabel from '@mui/material/InputLabel'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import SearchIcon from '@mui/icons-material/Search'
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { api, formatDate, plural, type MerchantItem } from './api'
import { gridLocale } from './gridLocale'
import { PageHeader } from './Layout'
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
const VALID_STATUSES = new Set(STATUS_FILTERS.map((f) => f.value))

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
    field: 'last_outcome', headerName: 'Poslední kontrola', width: 180,
    renderCell: ({ row }) => <StatusChip status={merchantStatus(row.last_outcome, row.open_alerts)} title={row.last_error} />,
  },
  {
    field: 'last_error', headerName: 'Chyba', flex: 1, minWidth: 200, valueGetter: (v) => v ?? '',
    renderCell: ({ value }) => <Typography variant="body2" color="text.secondary" noWrap title={value} sx={{ lineHeight: "60px" }}>{value}</Typography>,
  },
  {
    field: 'open_alerts', headerName: 'Nevyřízené nálezy', width: 160, align: 'center', headerAlign: 'center',
    renderCell: ({ row }) => (
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'center', height: '100%' }}>
        {row.open_alerts > 0 ? <StatusChip status="open" label={String(row.open_alerts)} /> : <Typography variant="body2" color="text.secondary">0</Typography>}
        {(row.open_alerts > 0 || row.last_outcome === 'found') && (
          <Link to={`/nalezy?merchant_id=${encodeURIComponent(row.id)}`}>nálezy</Link>
        )}
      </Stack>
    ),
  },
  { field: 'last_check_at', headerName: 'Zkontrolováno', width: 165, valueFormatter: (v) => formatDate(v) },
  { field: 'last_duration_ms', headerName: 'Doba', width: 90, valueFormatter: (v: number | null) => (v == null ? '–' : `${(v / 1000).toFixed(1)} s`) },
]

export default function MerchantsPage() {
  const [params, setParams] = useSearchParams()
  const urlStatus = params.get('status') ?? ''
  const status = VALID_STATUSES.has(urlStatus) ? urlStatus : ''
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 })

  useEffect(() => {
    const t = setTimeout(() => { setSearch(searchInput.trim()); setPagination((p) => ({ ...p, page: 0 })) }, 300)
    return () => clearTimeout(t)
  }, [searchInput])

  const setStatus = (v: string) => {
    setParams(v ? { status: v } : {})
    setPagination((p) => ({ ...p, page: 0 }))
  }

  const q = useQuery({
    queryKey: ['merchants', search, status, pagination],
    queryFn: () => api.merchants({ search, status, page: pagination.page + 1, pageSize: pagination.pageSize }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader title="Obchody" subtitle="Výsledek poslední kontroly každého obchodu. Nahoře jsou obchody, které potřebují pozornost." />
      <RunPanel />
      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2, alignItems: { sm: 'center' }, borderBottom: 1, borderColor: 'divider' }}>
          <TextField
            size="small" placeholder="Hledat obchod (ID nebo URL)" value={searchInput} onChange={(e) => setSearchInput(e.target.value)}
            sx={{ width: { sm: 320 } }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
          />
          <FormControl size="small" sx={{ width: { sm: 240 } }}>
            <InputLabel>Stav poslední kontroly</InputLabel>
            <Select label="Stav poslední kontroly" value={status} onChange={(e) => setStatus(e.target.value)}>
              {STATUS_FILTERS.map((f) => <MenuItem key={f.value} value={f.value}>{f.label}</MenuItem>)}
            </Select>
          </FormControl>
          <Box sx={{ flex: 1 }} />
          {q.data && <Typography variant="body2" color="text.secondary">{plural(q.data.total, 'obchod', 'obchody', 'obchodů')}</Typography>}
        </Stack>
        {q.isError ? (
          <Box sx={{ p: 2 }}><ErrorBox error={q.error} onRetry={() => q.refetch()} /></Box>
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
            rowHeight={60}
            autoHeight
            localeText={gridLocale(search || status ? 'Žádný obchod neodpovídá filtru.' : 'Zatím tu nejsou žádné obchody.')}
          />
        )}
      </Paper>
    </>
  )
}
