import ErrorOutline from '@mui/icons-material/ErrorOutlined'
import ScheduleOutlined from '@mui/icons-material/ScheduleOutlined'
import FiberManualRecord from '@mui/icons-material/FiberManualRecord'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Link from '@mui/material/Link'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { dashboardApi } from '../../api/endpoints'
import type { AgentStatus, TeamSummary, TicketListItem, TicketView } from '../../api/types'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { Person } from '../../components/Person'
import { useNow } from '../sla/slaFormat'
import { PriorityChip, SlaChip, StatusChip } from '../tickets/ticketUi'
import { FlowChart } from './FlowChart'

type Tone = 'critical' | 'warning' | undefined

/** One headline number. Status tones always come with an icon, never colour alone. */
function StatTile({ label, value, tone, to, view }: { label: string; value: number; tone?: Tone; to?: string; view?: TicketView }) {
  const active = tone && value > 0
  const color = active ? (tone === 'critical' ? 'error.main' : 'warning.dark') : 'text.primary'
  const body = (
    <>
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
        {active && (tone === 'critical' ? <ErrorOutline sx={{ fontSize: 16, color }} /> : <ScheduleOutlined sx={{ fontSize: 16, color }} />)}
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>{label}</Typography>
      </Stack>
      <Typography sx={{ fontSize: '1.875rem', fontWeight: 650, lineHeight: 1.2, mt: 0.5, color, fontVariantNumeric: 'tabular-nums' }}>{value}</Typography>
    </>
  )
  const sx = { p: 2, display: 'block', textDecoration: 'none', color: 'inherit', transition: 'border-color 120ms' } as const
  return to ? (
    <Paper variant="outlined" component={RouterLink} to={to} state={view ? { view } : undefined}
      sx={{ ...sx, '&:hover': { borderColor: 'primary.main' } }}>{body}</Paper>
  ) : (
    <Paper variant="outlined" sx={sx}>{body}</Paper>
  )
}

function Tiles({ children }: { children: ReactNode }) {
  return <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fill, minmax(170px, 1fr))' }}>{children}</Box>
}

function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Stack spacing={1.5} component="section" aria-label={title}>
      <Stack direction="row" sx={{ alignItems: 'center' }}>
        <Typography variant="h2" component="h2" sx={{ flexGrow: 1 }}>{title}</Typography>
        {action}
      </Stack>
      {children}
    </Stack>
  )
}

function Card({ title, children, sx }: { title: string; children: ReactNode; sx?: object }) {
  return (
    <Paper variant="outlined" sx={{ p: 2, minWidth: 0, ...sx }}>
      <Typography variant="subtitle2" component="h3" sx={{ mb: 1.5 }}>{title}</Typography>
      {children}
    </Paper>
  )
}

/** Compact ticket list for the dashboard: subject, number, status and (for support) SLA. */
function TicketMiniList({ items, empty, showSla }: { items: TicketListItem[]; empty: string; showSla: boolean }) {
  const now = useNow()
  if (items.length === 0) return <Typography variant="body2" sx={{ color: 'text.secondary', py: 1 }}>{empty}</Typography>
  return (
    <Stack divider={<Box sx={{ borderTop: 1, borderColor: 'divider' }} />}>
      {items.map((ticket) => (
        <Stack key={ticket.ticketId} direction={{ xs: 'column', sm: 'row' }} sx={{ py: 1.25, gap: 1, alignItems: { sm: 'center' } }}>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Link component={RouterLink} to={`/tickets/${ticket.ticketId}`} underline="hover" color="text.primary" sx={{ fontWeight: 600, display: 'block' }} noWrap>
              {ticket.subject}
            </Link>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              <Box component="span" sx={{ fontFamily: 'Consolas, ui-monospace, monospace', color: 'primary.main' }}>{ticket.trackingId}</Box> · {ticket.categoryName}
            </Typography>
          </Box>
          <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexShrink: 0 }}>
            {showSla && <SlaChip dueAt={ticket.slaDueAt} paused={ticket.slaPaused} escalationLevel={ticket.escalationLevel} now={now} />}
            <PriorityChip id={ticket.priorityId} name={ticket.priorityName} />
            <StatusChip status={ticket.status} />
          </Stack>
        </Stack>
      ))}
    </Stack>
  )
}

const statusDot: Record<AgentStatus, string> = { Available: 'success.main', Busy: 'error.main', Away: 'warning.main', Offline: 'text.disabled' }

function TeamSection({ team }: { team: TeamSummary }) {
  const { t } = useTranslation()
  const maxPriority = Math.max(1, ...team.openByPriority.map((p) => p.count))
  const maxLoad = Math.max(1, ...team.agents.map((a) => a.open))
  return (
    <Section title={t('dashboard.team')}>
      <Tiles>
        <StatTile label={t('dashboard.teamOpen')} value={team.open} to="/tickets" view="Team" />
        <StatTile label={t('dashboard.unassigned')} value={team.unassigned} tone="warning" to="/tickets" view="Unassigned" />
        <StatTile label={t('dashboard.overdue')} value={team.overdue} tone="critical" to="/tickets" view="Team" />
        <StatTile label={t('dashboard.escalated')} value={team.escalated} tone="critical" to="/tickets" view="Escalated" />
      </Tiles>
      <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' } }}>
        <Card title={t('dashboard.flowTitle')}><FlowChart days={team.last14Days} /></Card>
        <Card title={t('dashboard.byPriority')}>
          {/* One measure, one hue: bar length is the count, the number is printed beside it. */}
          <Stack spacing={1.5}>
            {team.openByPriority.map((p) => (
              <Box key={p.priorityId}>
                <Stack direction="row" sx={{ justifyContent: 'space-between', mb: 0.5 }}>
                  <Typography variant="body2">{p.priorityName}</Typography>
                  <Typography variant="body2" sx={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{p.count}</Typography>
                </Stack>
                <Box sx={{ height: 8, borderRadius: 4, bgcolor: 'action.hover' }}>
                  <Box sx={{ height: 8, borderRadius: 4, bgcolor: 'primary.main', width: `${(p.count / maxPriority) * 100}%`, minWidth: p.count ? 4 : 0 }} />
                </Box>
              </Box>
            ))}
          </Stack>
        </Card>
      </Box>
      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Typography variant="subtitle2" component="h3" sx={{ p: 2, pb: 1 }}>{t('dashboard.agents')}</Typography>
        {team.agents.length === 0 ? (
          <Typography variant="body2" sx={{ color: 'text.secondary', px: 2, pb: 2 }}>{t('dashboard.noAgents')}</Typography>
        ) : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label={t('dashboard.agents')}>
              <TableHead>
                <TableRow>
                  <TableCell>{t('dashboard.agent')}</TableCell>
                  <TableCell>{t('dashboard.status')}</TableCell>
                  <TableCell sx={{ width: '40%' }}>{t('dashboard.openTickets')}</TableCell>
                  <TableCell align="right">{t('dashboard.overdue')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {team.agents.map((a) => (
                  <TableRow key={a.userId} hover>
                    <TableCell><Person name={a.name} /></TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
                        <FiberManualRecord sx={{ fontSize: 10, color: statusDot[a.status] }} />
                        <Typography variant="body2">{t(`availability.${a.status}`)}</Typography>
                      </Stack>
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
                        <Box sx={{ flexGrow: 1, height: 6, borderRadius: 3, bgcolor: 'action.hover', maxWidth: 220 }}>
                          <Box sx={{ height: 6, borderRadius: 3, bgcolor: 'primary.main', width: `${(a.open / maxLoad) * 100}%`, minWidth: a.open ? 4 : 0 }} />
                        </Box>
                        <Typography variant="body2" sx={{ fontVariantNumeric: 'tabular-nums', minWidth: 24 }}>{a.open}</Typography>
                      </Stack>
                    </TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums', color: a.overdue ? 'error.main' : 'text.secondary', fontWeight: a.overdue ? 600 : 400 }}>
                      {a.overdue}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        )}
      </Paper>
    </Section>
  )
}

/** One home page for everyone. Sections appear only for what the person can do: requests, own work, team. */
export function HomePage() {
  const { t } = useTranslation()
  const { me } = useAuth()
  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: dashboardApi.get, refetchInterval: 60_000 })
  const d = dashboard.data
  const now = useNow(60_000)
  const today = new Intl.DateTimeFormat(undefined, { weekday: 'long', day: 'numeric', month: 'long' }).format(now)

  const requestsSection = d && (
    <Section title={t('dashboard.myRequests')}
      action={<Button component={RouterLink} to="/requests">{t('dashboard.viewAll')}</Button>}>
      <Tiles>
        <StatTile label={t('dashboard.openRequests')} value={d.requests.open} to="/requests" />
        <StatTile label={t('dashboard.awaitingYou')} value={d.requests.awaitingYou} tone="warning" to="/requests" />
        <StatTile label={t('dashboard.resolved30')} value={d.requests.resolvedLast30Days} />
      </Tiles>
      <Card title={t('dashboard.recentRequests')}>
        <TicketMiniList items={d.requests.recent} empty={t('dashboard.noRequests')} showSla={false} />
      </Card>
    </Section>
  )
  // Support staff with no requests of their own don't need that section taking up the top of the page.
  const hasRequests = !!d && (d.requests.recent.length > 0 || !d.work)

  return (
    <Stack spacing={4} sx={{ maxWidth: 1280 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-end', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>{today}</Typography>
          <Typography variant="h1" component="h1">{t('home.greeting', { name: me?.displayName })}</Typography>
        </Box>
        <Button variant="contained" component={RouterLink} to="/requests/new">{t('nav.newRequest')}</Button>
      </Stack>

      <ErrorAlert error={dashboard.error} />
      {dashboard.isLoading && <CircularProgress aria-label={t('app.loading')} />}

      {d?.work && (
        <Section title={t('dashboard.myWork')}
          action={<Button component={RouterLink} to="/tickets">{t('dashboard.openQueues')}</Button>}>
          <Tiles>
            <StatTile label={t('dashboard.assignedToMe')} value={d.work.assignedOpen} to="/tickets" view="Mine" />
            <StatTile label={t('dashboard.overdue')} value={d.work.overdue} tone="critical" to="/tickets" view="Mine" />
            <StatTile label={t('dashboard.dueSoon')} value={d.work.dueWithinHour} tone="warning" to="/tickets" view="Mine" />
            {d.work.unassigned !== null && <StatTile label={t('dashboard.unassignedQueue')} value={d.work.unassigned} to="/tickets" view="Unassigned" />}
            {d.work.escalated !== null && <StatTile label={t('dashboard.escalated')} value={d.work.escalated} tone="critical" to="/tickets" view="Escalated" />}
          </Tiles>
          <Card title={t('dashboard.nextUp')}>
            <TicketMiniList items={d.work.nextUp} empty={t('dashboard.nothingAssigned')} showSla />
          </Card>
        </Section>
      )}

      {d?.team && <TeamSection team={d.team} />}

      {hasRequests && requestsSection}
    </Stack>
  )
}
