import InboxOutlined from '@mui/icons-material/InboxOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import InputAdornment from '@mui/material/InputAdornment'
import LinearProgress from '@mui/material/LinearProgress'
import Link from '@mui/material/Link'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Tab from '@mui/material/Tab'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import Tabs from '@mui/material/Tabs'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink, useLocation, useNavigate } from 'react-router'
import { ticketApi } from '../../api/endpoints'
import type { TicketView } from '../../api/types'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { Person } from '../../components/Person'
import { useNow } from '../sla/slaFormat'
import { formatWhen } from './ticketFormat'
import { PriorityChip, SlaChip, StatusChip } from './ticketUi'

const mono = 'Consolas, ui-monospace, monospace'

/**
 * mode "requests": the signed-in person's own requests.
 * mode "queues": support queues, tabs shown only for permissions the user holds.
 */
export function TicketListPage({ mode }: { mode: 'requests' | 'queues' }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()

  const views: TicketView[] =
    mode === 'requests'
      ? ['Requested']
      : (['Mine', 'Team', 'Unassigned', 'Escalated', 'Deleted'] as TicketView[]).filter(
          (v) =>
            v === 'Mine' ||
            (v === 'Team' && can('ticket.view')) ||
            (v === 'Unassigned' && can('queue.unassigned')) ||
            (v === 'Escalated' && can('queue.escalated')) ||
            (v === 'Deleted' && can('ticket.restore')),
        )
  const linkState = useLocation().state as { notice?: string; view?: TicketView } | null
  const notice = linkState?.notice

  // Links (e.g. from the home dashboard) can open a specific tab.
  const [view, setView] = useState<TicketView>(linkState?.view && views.includes(linkState.view) ? linkState.view : views[0])
  const [status, setStatus] = useState('open')
  const [q, setQ] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)

  const list = useQuery({
    queryKey: ['tickets', view, status, search, page, pageSize],
    queryFn: () => ticketApi.list({ view, status, q: search, page: page + 1, pageSize }),
    placeholderData: keepPreviousData,
  })

  const showRequester = mode === 'queues'
  const now = useNow()

  return (
    <Stack spacing={2.5}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{mode === 'requests' ? t('tickets.myRequests') : t('tickets.queues')}</Typography>
        </Box>
        <Button variant="contained" component={RouterLink} to="/requests/new">
          {mode === 'requests' ? t('tickets.newRequest') : t('tickets.newTicket')}
        </Button>
      </Stack>

      {notice && <Alert severity="info">{notice}</Alert>}

      {views.length > 1 && (
        <Tabs value={view} onChange={(_, v: TicketView) => { setView(v); setPage(0) }} variant="scrollable" allowScrollButtonsMobile>
          {views.map((v) => <Tab key={v} value={v} label={t(`tickets.view.${v}`)} />)}
        </Tabs>
      )}

      <ErrorAlert error={list.error} />

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        {/* Toolbar sits on the grid card so filters read as part of the list they filter. */}
        <Stack component="form" direction={{ xs: 'column', sm: 'row' }} spacing={1.5}
          sx={{ p: 2, alignItems: { sm: 'center' }, borderBottom: 1, borderColor: 'divider' }}
          onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()); setPage(0) }}>
          <TextField id="tickets-search" label={t('tickets.search')} value={q} onChange={(e) => setQ(e.target.value)} sx={{ maxWidth: { sm: 360 } }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchOutlined fontSize="small" /></InputAdornment> } }} />
          <TextField id="tickets-status" select label={t('tickets.statusFilter')} value={status}
            onChange={(e) => { setStatus(e.target.value); setPage(0) }} sx={{ maxWidth: { sm: 200 } }}>
            <MenuItem value="open">{t('tickets.filter.open')}</MenuItem>
            <MenuItem value="done">{t('tickets.filter.done')}</MenuItem>
            <MenuItem value="all">{t('tickets.filter.all')}</MenuItem>
          </TextField>
          <Button type="submit" variant="outlined">{t('tickets.searchButton')}</Button>
          <Box sx={{ flexGrow: 1 }} />
          {list.data && (
            <Chip size="small" variant="outlined" label={t('tickets.count', { count: list.data.total })} sx={{ alignSelf: { xs: 'flex-start', sm: 'center' } }} />
          )}
        </Stack>
        <Box sx={{ height: 2 }}>{list.isFetching && <LinearProgress sx={{ height: 2 }} />}</Box>

        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t('tickets.tableLabel')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('tickets.col.ticket')}</TableCell>
                <TableCell>{t('tickets.col.status')}</TableCell>
                <TableCell>{t('tickets.col.priority')}</TableCell>
                {showRequester && <TableCell>{t('tickets.col.sla')}</TableCell>}
                {showRequester && <TableCell>{t('tickets.col.requester')}</TableCell>}
                <TableCell>{showRequester ? t('tickets.col.team') : t('tickets.col.desk')}</TableCell>
                <TableCell>{t('tickets.col.assignee')}</TableCell>
                <TableCell align="right">{t('tickets.col.updated')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {list.data?.items.map((ticket) => (
                // The whole row opens the ticket; the subject stays a real link for keyboard and screen-reader users.
                <TableRow key={ticket.ticketId} hover onClick={() => navigate(`/tickets/${ticket.ticketId}`)} sx={{ cursor: 'pointer' }}>
                  <TableCell sx={{ minWidth: 280, maxWidth: 420 }}>
                    <Link component={RouterLink} to={`/tickets/${ticket.ticketId}`} underline="hover" color="text.primary"
                      onClick={(e) => e.stopPropagation()} sx={{ fontWeight: 600, display: 'block', overflowWrap: 'anywhere' }}>
                      {ticket.subject}
                    </Link>
                    <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                      <Box component="span" sx={{ fontFamily: mono, color: 'primary.main' }}>{ticket.trackingId}</Box> · {ticket.categoryName}
                    </Typography>
                  </TableCell>
                  <TableCell><StatusChip status={ticket.status} /></TableCell>
                  <TableCell><PriorityChip id={ticket.priorityId} name={ticket.priorityName} /></TableCell>
                  {showRequester && (
                    <TableCell>
                      <SlaChip dueAt={ticket.slaDueAt} paused={ticket.slaPaused} escalationLevel={ticket.escalationLevel} now={now} />
                    </TableCell>
                  )}
                  {showRequester && <TableCell sx={{ maxWidth: 200 }}><Person name={ticket.requesterName} /></TableCell>}
                  <TableCell sx={{ color: 'text.secondary' }}>{showRequester ? ticket.teamName : ticket.serviceDeskName}</TableCell>
                  <TableCell sx={{ maxWidth: 200 }}>
                    {ticket.assigneeName
                      ? <Person name={ticket.assigneeName} />
                      : <Typography variant="body2" sx={{ color: 'text.disabled', fontStyle: 'italic' }}>{t('tickets.unassigned')}</Typography>}
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', color: 'text.secondary' }}>
                    {formatWhen(ticket.updatedAt)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
        {list.data && list.data.total === 0 && (
          <Stack spacing={1} sx={{ py: 6, px: 3, alignItems: 'center', textAlign: 'center' }}>
            <InboxOutlined sx={{ fontSize: 40, color: 'text.disabled' }} />
            <Typography sx={{ color: 'text.secondary' }}>
              {mode === 'requests' ? t('tickets.emptyRequests') : t('tickets.emptyQueue')}
            </Typography>
          </Stack>
        )}
        <TablePagination
          component="div"
          count={list.data?.total ?? 0}
          page={page}
          onPageChange={(_, p) => setPage(p)}
          rowsPerPage={pageSize}
          onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0) }}
          rowsPerPageOptions={[10, 25, 50]}
        />
      </Paper>
    </Stack>
  )
}
