import { useState } from 'react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { KIND_LABELS, api, formatDate, plural, type AlertItem } from './api'
import AlertDrawer from './AlertDrawer'
import { gridLocale } from './gridLocale'
import { PageHeader } from './Layout'
import { ErrorBox } from './QueryState'
import StatCards from './StatCards'
import StatusChip from './StatusChip'
import { useSummary } from './useSummary'

type Filter = 'open' | 'resolved' | 'all'

/** Shortens in the middle so both host and file name stay visible: https://evil.example/…/skimmer.js */
export function middleEllipsis(s: string, max = 70) {
  if (s.length <= max) return s
  const keep = max - 1
  return s.slice(0, Math.ceil(keep * 0.55)) + '…' + s.slice(s.length - Math.floor(keep * 0.45))
}

const columns: GridColDef<AlertItem>[] = [
  {
    field: 'merchant_id', headerName: 'Obchod', width: 190,
    renderCell: ({ row }) => (
      <Stack sx={{ justifyContent: 'center', height: '100%', lineHeight: 1.3 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>{row.merchant_id}</Typography>
        <Typography variant="caption" color="text.secondary" noWrap>{row.merchant_url.replace(/^https?:\/\//, '')}</Typography>
      </Stack>
    ),
  },
  {
    field: 'url', headerName: 'Škodlivá adresa', flex: 1, minWidth: 300,
    renderCell: ({ value }) => (
      <Tooltip title={value}>
        <Typography variant="body2" sx={{ fontFamily: 'ui-monospace, "Cascadia Mono", Consolas, monospace', fontSize: 13, whiteSpace: 'nowrap', lineHeight: '60px' }}>
          {middleEllipsis(value)}
        </Typography>
      </Tooltip>
    ),
  },
  { field: 'kind', headerName: 'Typ', width: 140, valueFormatter: (v: string) => KIND_LABELS[v] ?? v },
  { field: 'first_seen_at', headerName: 'Poprvé', width: 160, valueFormatter: (v) => formatDate(v) },
  { field: 'last_seen_at', headerName: 'Naposledy', width: 160, valueFormatter: (v) => formatDate(v) },
  {
    field: 'resolved', headerName: 'Stav', width: 130,
    renderCell: ({ row }) => <StatusChip status={row.resolved ? 'resolved' : 'open'} title={row.resolved && row.note ? row.note : null} />,
  },
]

export default function AlertsPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const merchantId = params.get('merchant_id') ?? ''
  // Default view: unresolved findings; for one merchant show all of its findings (incl. resolved history).
  const [filter, setFilter] = useState<Filter>(merchantId ? 'all' : 'open')
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 })
  const [selected, setSelected] = useState<AlertItem | null>(null)
  const [lastMerchant, setLastMerchant] = useState(merchantId)
  if (lastMerchant !== merchantId) { // merchant filter changed (link or chip removed): reset view
    setLastMerchant(merchantId)
    setFilter(merchantId ? 'all' : 'open')
    setPagination((p) => ({ ...p, page: 0 }))
  }
  const changeFilter = (v: Filter) => { setFilter(v); setPagination((p) => ({ ...p, page: 0 })) }

  const q = useQuery({
    queryKey: ['alerts', filter, merchantId, pagination],
    queryFn: () => api.alerts({
      resolved: filter === 'all' ? undefined : filter === 'resolved',
      merchant_id: merchantId || undefined,
      page: pagination.page + 1,
      pageSize: pagination.pageSize,
    }),
    placeholderData: keepPreviousData,
  })
  const s = useSummary().data
  const failed = s ? s.timeout + s.unreachable : 0

  return (
    <>
      <PageHeader title="Nálezy" subtitle="Škodlivé adresy ze seznamu, které úvodní stránky obchodů načítají nebo na ně odkazují." />

      <StatCards
        stats={[
          { key: 'open', label: 'Nevyřízené nálezy', value: s?.openAlerts, color: 'red', hint: 'k řešení', onClick: () => { setParams({}); changeFilter('open') }, active: filter === 'open' && !merchantId },
          { key: 'found', label: 'Obchody s nálezem', value: s?.found, color: 'red', hint: 'poslední kontrola', onClick: () => navigate('/obchody?status=found') },
          { key: 'timeout', label: 'Nedokončeno', value: s?.timeout, color: 'yellow', hint: 'kontrola nestihla 10 s', onClick: () => navigate('/obchody?status=timeout') },
          { key: 'unreachable', label: 'Nedostupné', value: s?.unreachable, color: 'orange', hint: 'chyba / blokace', onClick: () => navigate('/obchody?status=unreachable') },
          { key: 'ok', label: 'OK bez nálezu', value: s?.ok, color: 'green', hint: 'kontrola dokončena', onClick: () => navigate('/obchody?status=ok') },
          { key: 'unchecked', label: 'Nezkontrolováno', value: s?.unchecked, color: 'grey', hint: 'zatím bez kontroly', onClick: () => navigate('/obchody?status=unchecked') },
        ]}
      />

      {s && (failed > 0 || s.unchecked > 0) && (
        <Alert severity="warning" variant="outlined" sx={{ mb: 3, bgcolor: '#fffbeb' }}>
          {failed > 0 && <>U <b>{failed}</b> obchodů poslední kontrola neproběhla celá ({s.timeout}× nedokončeno, {s.unreachable}× nedostupné). </>}
          {s.unchecked > 0 && <><b>{s.unchecked}</b> obchodů ještě nebylo zkontrolováno. </>}
          Chybějící nález u nich neznamená, že jsou v pořádku. <Link to="/obchody">Zobrazit obchody</Link>
        </Alert>
      )}

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2, alignItems: { sm: 'center' }, borderBottom: 1, borderColor: 'divider' }}>
          <ToggleButtonGroup size="small" exclusive value={filter} onChange={(_, v: Filter | null) => v && changeFilter(v)}>
            <ToggleButton value="open">Nevyřízené</ToggleButton>
            <ToggleButton value="resolved">Vyřízené</ToggleButton>
            <ToggleButton value="all">Vše</ToggleButton>
          </ToggleButtonGroup>
          {merchantId && <Chip label={`Obchod: ${merchantId}`} onDelete={() => setParams({})} color="primary" variant="outlined" />}
          <Box sx={{ flex: 1 }} />
          {q.data && <Typography variant="body2" color="text.secondary">{plural(q.data.total, 'nález', 'nálezy', 'nálezů')} · klikněte na řádek pro detail</Typography>}
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
            onRowClick={({ row }) => setSelected(row)}
            rowHeight={60}
            autoHeight
            sx={{ '& .MuiDataGrid-row': { cursor: 'pointer' } }}
            localeText={gridLocale(filter === 'open' ? 'Žádné nevyřízené nálezy.' : filter === 'resolved' ? 'Zatím žádné vyřízené nálezy.' : 'Zatím žádné nálezy.')}
          />
        )}
      </Paper>
      <AlertDrawer alert={selected} onClose={() => setSelected(null)} />
    </>
  )
}
