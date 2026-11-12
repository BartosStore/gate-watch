import { useState } from 'react'
import Alert from '@mui/material/Alert'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Typography from '@mui/material/Typography'
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { KIND_LABELS, api, formatDate, type AlertItem } from './api'
import AlertDrawer from './AlertDrawer'
import { gridLocale } from './gridLocale'
import { ErrorBox } from './QueryState'
import StatusChip from './StatusChip'

type Filter = 'open' | 'resolved' | 'all'

const columns: GridColDef<AlertItem>[] = [
  { field: 'merchant_id', headerName: 'Obchod', width: 170 },
  {
    field: 'url', headerName: 'Škodlivá adresa', flex: 1, minWidth: 300,
    renderCell: ({ value }) => <Typography variant="body2" sx={{ fontFamily: 'monospace', overflow: 'hidden', textOverflow: 'ellipsis' }}>{value}</Typography>,
  },
  { field: 'kind', headerName: 'Typ', width: 140, valueFormatter: (v: string) => KIND_LABELS[v] ?? v },
  { field: 'first_seen_at', headerName: 'Poprvé', width: 165, valueFormatter: (v) => formatDate(v) },
  { field: 'last_seen_at', headerName: 'Naposledy', width: 165, valueFormatter: (v) => formatDate(v) },
  {
    field: 'resolved', headerName: 'Stav', width: 130,
    renderCell: ({ row }) => <StatusChip status={row.resolved ? 'resolved' : 'open'} title={row.resolved && row.note ? row.note : null} />,
  },
]

/** Counts of merchants whose last check did not finish — an empty finding list is not proof of safety. */
function useCheckSummary() {
  return useQuery({
    queryKey: ['summary'],
    queryFn: async () => {
      const [timeout, unreachable, unchecked] = await Promise.all(
        ['timeout', 'unreachable', 'unchecked'].map((status) => api.merchants({ status, page: 1, pageSize: 1 }).then((p) => p.total)),
      )
      return { timeout, unreachable, unchecked }
    },
  })
}

export default function AlertsPage() {
  const [filter, setFilter] = useState<Filter>('open')
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 })
  const [selected, setSelected] = useState<AlertItem | null>(null)

  const q = useQuery({
    queryKey: ['alerts', filter, pagination],
    queryFn: () => api.alerts({ resolved: filter === 'all' ? undefined : filter === 'resolved', page: pagination.page + 1, pageSize: pagination.pageSize }),
    placeholderData: keepPreviousData,
  })
  const summary = useCheckSummary()
  const s = summary.data
  const failed = s ? s.timeout + s.unreachable : 0

  return (
    <Paper sx={{ p: 2 }}>
      <Stack direction="row" spacing={2} sx={{ mb: 2, alignItems: 'center' }}>
        <ToggleButtonGroup size="small" exclusive value={filter} onChange={(_, v: Filter | null) => { if (v) { setFilter(v); setPagination((p) => ({ ...p, page: 0 })) } }}>
          <ToggleButton value="open">Nevyřízené</ToggleButton>
          <ToggleButton value="resolved">Vyřízené</ToggleButton>
          <ToggleButton value="all">Vše</ToggleButton>
        </ToggleButtonGroup>
        {q.data && <Typography variant="body2" color="text.secondary">{q.data.total} nálezů</Typography>}
      </Stack>

      {s && (failed > 0 || s.unchecked > 0) && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {failed > 0 && <>U <b>{failed}</b> obchodů poslední kontrola neproběhla celá ({s.timeout}× nedokončeno, {s.unreachable}× nedostupné). </>}
          {s.unchecked > 0 && <><b>{s.unchecked}</b> obchodů ještě nebylo zkontrolováno. </>}
          Chybějící nález u nich neznamená, že jsou v pořádku. <Link to="/obchody">Zobrazit obchody</Link>
        </Alert>
      )}

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
          onRowClick={({ row }) => setSelected(row)}
          autoHeight
          sx={{ '& .MuiDataGrid-row': { cursor: 'pointer' } }}
          localeText={gridLocale(filter === 'open' ? 'Žádné nevyřízené nálezy.' : filter === 'resolved' ? 'Zatím žádné vyřízené nálezy.' : 'Zatím žádné nálezy.')}
        />
      )}
      <AlertDrawer alert={selected} onClose={() => setSelected(null)} />
    </Paper>
  )
}
