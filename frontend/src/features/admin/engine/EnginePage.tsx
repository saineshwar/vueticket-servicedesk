import CheckCircleOutline from '@mui/icons-material/CheckCircleOutlined'
import ErrorOutline from '@mui/icons-material/ErrorOutlined'
import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import FormControlLabel from '@mui/material/FormControlLabel'
import LinearProgress from '@mui/material/LinearProgress'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { engineApi } from '../../../api/endpoints'
import type { EngineHealth, EngineTaskState } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { formatMinutes, parseUtc, useNow } from '../../sla/slaFormat'
import { formatWhen } from '../../tickets/ticketFormat'

const healthChip: Record<EngineHealth, { color: 'success' | 'warning' | 'error' | 'default'; variant: 'filled' | 'outlined' }> = {
  Running: { color: 'success', variant: 'filled' },
  Late: { color: 'warning', variant: 'filled' },
  Failing: { color: 'error', variant: 'filled' },
  Stopped: { color: 'default', variant: 'filled' },
  Disabled: { color: 'default', variant: 'outlined' },
}

const taskOrder = ['sla-events', 'sla-monitor', 'escalation', 'assignment', 'unassigned-watch', 'auto-close', 'log-cleanup']
const mono = 'Consolas, ui-monospace, monospace'

/** "12s ago", "in 3m": seconds matter here because tasks run every few seconds. */
function useRelative() {
  const { t } = useTranslation()
  return (iso: string, now: number) => {
    const seconds = Math.round((parseUtc(iso).getTime() - now) / 1000)
    const abs = Math.abs(seconds)
    const text = abs < 60 ? `${abs}s` : formatMinutes(abs / 60)
    if (abs < 2) return t('engine.justNow')
    return seconds < 0 ? t('engine.ago', { time: text }) : t('engine.in', { time: text })
  }
}

function useEvery() {
  const { t } = useTranslation()
  return (seconds: number) =>
    seconds < 60 ? t('engine.everySeconds', { count: seconds }) : t('engine.every', { time: formatMinutes(seconds / 60) })
}

export function EnginePage() {
  const { t } = useTranslation()
  // Refresh often: the page is for checking whether the worker is alive right now.
  const overview = useQuery({ queryKey: ['engine'], queryFn: engineApi.overview, refetchInterval: 10_000 })
  const now = useNow(1_000)
  const relative = useRelative()

  const tasks = [...(overview.data?.tasks ?? [])].sort((a, b) =>
    b.instanceStartedAt.localeCompare(a.instanceStartedAt) || taskOrder.indexOf(a.task) - taskOrder.indexOf(b.task))
  const live = tasks.filter((x) => x.health !== 'Stopped')
  const attention = live.filter((x) => x.health === 'Late' || x.health === 'Failing')
  const instances = [...new Set(live.map((x) => x.instanceId))]
  const stream = overview.data?.stream

  return (
    <Stack spacing={2.5} sx={{ maxWidth: 1280 }}>
      <Box>
        <Typography variant="h1" component="h1">{t('engine.title')}</Typography>
        <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('engine.intro')}</Typography>
      </Box>
      <ErrorAlert error={overview.error} />

      {overview.data && (
        <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' }}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('engine.overall')}</Typography>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 0.75 }}>
              {instances.length === 0 || attention.length > 0
                ? <ErrorOutline sx={{ color: 'error.main' }} />
                : <CheckCircleOutline sx={{ color: 'success.main' }} />}
              <Typography sx={{ fontWeight: 650, fontSize: '1.15rem' }}>
                {instances.length === 0 ? t('engine.notRunning') : attention.length > 0 ? t('engine.needsAttention', { count: attention.length }) : t('engine.allRunning')}
              </Typography>
            </Stack>
          </Paper>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('engine.workers')}</Typography>
            <Typography sx={{ fontWeight: 650, fontSize: '1.875rem', lineHeight: 1.2, mt: 0.5 }}>{instances.length}</Typography>
            <Typography variant="caption" sx={{ color: 'text.secondary', fontFamily: mono }}>{instances.join(', ') || '—'}</Typography>
          </Paper>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('engine.backlog')}</Typography>
            <Typography sx={{ fontWeight: 650, fontSize: '1.875rem', lineHeight: 1.2, mt: 0.5, color: (stream?.backlog ?? 0) > 50 ? 'warning.dark' : 'text.primary' }}>
              {stream?.backlog ?? '—'}
            </Typography>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {stream ? t('engine.readUpTo', { id: stream.lastEventId, latest: stream.latestEventId }) : ''}
            </Typography>
          </Paper>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('engine.reader')}</Typography>
            <Typography sx={{ fontWeight: 600, mt: 0.75, fontFamily: mono, fontSize: '0.9rem' }}>{stream?.leaseOwner ?? t('engine.noReader')}</Typography>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {stream?.updatedAt ? t('engine.checkpointSaved', { when: relative(stream.updatedAt, now) }) : ''}
            </Typography>
          </Paper>
        </Box>
      )}

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Stack direction="row" sx={{ p: 2, alignItems: 'center', borderBottom: 1, borderColor: 'divider' }}>
          <Typography variant="subtitle2" component="h2" sx={{ flexGrow: 1 }}>{t('engine.tasks')}</Typography>
          {overview.data && <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('engine.refreshed', { when: relative(overview.data.now, now) })}</Typography>}
        </Stack>
        <Box sx={{ height: 2 }}>{overview.isFetching && <LinearProgress sx={{ height: 2 }} />}</Box>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t('engine.tasks')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('engine.task')}</TableCell>
                <TableCell>{t('engine.health')}</TableCell>
                <TableCell>{t('engine.lastRun')}</TableCell>
                <TableCell>{t('engine.nextRun')}</TableCell>
                <TableCell align="right">{t('engine.lastResult')}</TableCell>
                <TableCell align="right">{t('engine.runsFailures')}</TableCell>
                <TableCell>{t('engine.lastError')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {tasks.map((x) => <TaskRow key={`${x.instanceId}-${x.task}`} task={x} now={now} showInstance={instances.length > 1 || x.health === 'Stopped'} />)}
            </TableBody>
          </Table>
        </Box>
        {overview.data && tasks.length === 0 && (
          <Typography sx={{ p: 3, color: 'text.secondary' }}>{t('engine.neverRan')}</Typography>
        )}
      </Paper>

      <RunLog />
    </Stack>
  )
}

function TaskRow({ task: x, now, showInstance }: { task: EngineTaskState; now: number; showInstance: boolean }) {
  const { t } = useTranslation()
  const relative = useRelative()
  const every = useEvery()
  const chip = healthChip[x.health]
  const errorIsCurrent = x.health === 'Failing'
  return (
    <TableRow hover sx={{ opacity: x.health === 'Stopped' ? 0.6 : 1 }}>
      <TableCell sx={{ minWidth: 220 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>{t(`engine.taskName.${x.task}`, { defaultValue: x.task })}</Typography>
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>
          {t(`engine.taskHelp.${x.task}`, { defaultValue: '' })} · {every(x.intervalSeconds)}
          {showInstance && <> · <Box component="span" sx={{ fontFamily: mono }}>{x.instanceId}</Box></>}
        </Typography>
      </TableCell>
      <TableCell><Chip size="small" color={chip.color} variant={chip.variant} label={t(`engine.state.${x.health}`)} /></TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>
        {x.lastRunAt ? (
          <Tooltip title={formatWhen(x.lastRunAt)}><span>{relative(x.lastRunAt, now)}</span></Tooltip>
        ) : <Typography variant="body2" sx={{ color: 'text.disabled' }}>{t('engine.notYet')}</Typography>}
      </TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap', color: 'text.secondary' }}>
        {x.nextRunAt ? relative(x.nextRunAt, now) : '—'}
      </TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
        {x.lastRunAt ? t('engine.result', { count: x.lastProcessed, ms: x.lastDurationMs ?? 0 }) : '—'}
      </TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
        {x.runs.toLocaleString()} / <Box component="span" sx={{ color: x.failures ? 'error.main' : 'text.secondary', fontWeight: x.failures ? 600 : 400 }}>{x.failures.toLocaleString()}</Box>
      </TableCell>
      <TableCell sx={{ maxWidth: 320 }}>
        {x.lastError ? (
          <Tooltip title={x.lastError}>
            <Typography variant="body2" noWrap sx={{ color: errorIsCurrent ? 'error.main' : 'text.secondary' }}>
              {x.lastErrorAt && `${formatWhen(x.lastErrorAt)}: `}{x.lastError}
            </Typography>
          </Tooltip>
        ) : <Typography variant="body2" sx={{ color: 'text.disabled' }}>—</Typography>}
      </TableCell>
    </TableRow>
  )
}

function RunLog() {
  const { t } = useTranslation()
  const [task, setTask] = useState('all')
  const [errorsOnly, setErrorsOnly] = useState(false)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const runs = useQuery({
    queryKey: ['engine-runs', task, errorsOnly, page, pageSize],
    queryFn: () => engineApi.runs({ task: task === 'all' ? undefined : task, errorsOnly, page: page + 1, pageSize }),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  })

  return (
    <Paper variant="outlined" sx={{ overflow: 'hidden' }} component="section" aria-label={t('engine.log')}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ p: 2, alignItems: { sm: 'center' }, borderBottom: 1, borderColor: 'divider' }}>
        <Box sx={{ flexGrow: 1 }}>
          <Typography variant="subtitle2" component="h2">{t('engine.log')}</Typography>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('engine.logHelp')}</Typography>
        </Box>
        <TextField id="engine-log-task" select label={t('engine.task')} value={task} onChange={(e) => { setTask(e.target.value); setPage(0) }} sx={{ width: { sm: 220 } }}>
          <MenuItem value="all">{t('engine.allTasks')}</MenuItem>
          {taskOrder.map((x) => <MenuItem key={x} value={x}>{t(`engine.taskName.${x}`)}</MenuItem>)}
        </TextField>
        <FormControlLabel control={<Switch checked={errorsOnly} onChange={(e) => { setErrorsOnly(e.target.checked); setPage(0) }} />} label={t('engine.errorsOnly')} />
      </Stack>
      <ErrorAlert error={runs.error} />
      <Box sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label={t('engine.log')}>
          <TableHead>
            <TableRow>
              <TableCell>{t('engine.when')}</TableCell>
              <TableCell>{t('engine.task')}</TableCell>
              <TableCell>{t('engine.outcome')}</TableCell>
              <TableCell align="right">{t('engine.processed')}</TableCell>
              <TableCell align="right">{t('engine.duration')}</TableCell>
              <TableCell>{t('engine.details')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {runs.data?.items.map((r) => (
              <TableRow key={r.engineRunLogId} hover>
                <TableCell sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{formatWhen(r.startedAt)}</TableCell>
                <TableCell>{t(`engine.taskName.${r.task}`, { defaultValue: r.task })}</TableCell>
                <TableCell>
                  <Chip size="small" color={r.succeeded ? 'success' : 'error'} label={r.succeeded ? t('engine.ok') : t('engine.failed')} />
                </TableCell>
                <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{r.processed}</TableCell>
                <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums', color: 'text.secondary' }}>{r.durationMs} ms</TableCell>
                <TableCell sx={{ maxWidth: 460 }}>
                  {r.error
                    ? <Typography variant="body2" sx={{ color: 'error.main', overflowWrap: 'anywhere' }}>{r.error}</Typography>
                    : <Typography variant="body2" sx={{ color: 'text.secondary', fontFamily: mono, fontSize: '0.8rem' }}>{r.instanceId}</Typography>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Box>
      {runs.data?.total === 0 && <Typography sx={{ p: 3, color: 'text.secondary' }}>{errorsOnly ? t('engine.noErrors') : t('engine.noRuns')}</Typography>}
      <TablePagination component="div" count={runs.data?.total ?? 0} page={page} onPageChange={(_, p) => setPage(p)}
        rowsPerPage={pageSize} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0) }} rowsPerPageOptions={[25, 50, 100]} />
    </Paper>
  )
}
