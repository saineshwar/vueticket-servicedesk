import Alert from '@mui/material/Alert'
import AlertTitle from '@mui/material/AlertTitle'
import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'
import type { SlaClock, TicketEscalation, TicketSla } from '../../api/types'
import { dueState, useDueText, useNow } from '../sla/slaFormat'
import { formatWhen } from './ticketFormat'

/** Shown to support staff above the conversation while a ticket is escalated. */
export function EscalationBanner({ escalation }: { escalation: TicketEscalation }) {
  const { t } = useTranslation()
  const reasons = escalation.reasons.map((r) => t(`sla.clock.${r}`)).join(', ')
  return (
    <Alert severity="error" variant="outlined">
      <AlertTitle>{t('sla.escalationTitle', { level: escalation.level, role: escalation.roleName ?? '' })}</AlertTitle>
      {t('sla.escalationBody', { reasons: reasons.toLowerCase(), owner: escalation.ownerName ?? t('sla.noOwner'), since: formatWhen(escalation.startedAt) })}
      {escalation.windowEndsAt && <> {t('sla.escalationNext', { when: formatWhen(escalation.windowEndsAt) })}</>}
    </Alert>
  )
}

/** SLA targets for the ticket, with live countdowns. */
export function SlaPanel({ sla }: { sla: TicketSla }) {
  const { t } = useTranslation()
  const now = useNow()
  return (
    <Paper variant="outlined" sx={{ p: 2 }} component="section" aria-label={t('sla.title')}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline', mb: 1 }}>
        <Typography variant="subtitle2" component="h2">{t('sla.title')}</Typography>
        {sla.policyName && <Typography variant="caption" sx={{ color: 'text.secondary' }}>{sla.policyName}</Typography>}
      </Stack>
      {sla.pausedAt && (
        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1 }}>{t('sla.pausedSince', { when: formatWhen(sla.pausedAt) })}</Typography>
      )}
      <Stack spacing={1}>
        {sla.clocks.map((c) => <ClockRow key={c.clock} clock={c} paused={!!sla.pausedAt} now={now} />)}
      </Stack>
    </Paper>
  )
}

function ClockRow({ clock, paused, now }: { clock: SlaClock; paused: boolean; now: number }) {
  const { t } = useTranslation()
  const dueText = useDueText()

  let chip
  if (clock.metAt) {
    const late = clock.breachedAt !== null
    chip = <Chip size="small" color={late ? 'warning' : 'success'} variant="outlined" label={late ? t('sla.metLate') : t('sla.met')} />
  } else if (clock.breachedAt) {
    chip = <Chip size="small" color="error" label={t('sla.breached')} />
  } else if (paused) {
    chip = <Chip size="small" variant="outlined" label={t('sla.paused')} />
  } else if (clock.dueAt) {
    const state = dueState(clock.dueAt, now)
    chip = <Chip size="small" color={state === 'overdue' ? 'error' : state === 'soon' ? 'warning' : 'default'}
      variant={state === 'ok' ? 'outlined' : 'filled'} label={dueText(clock.dueAt, now)} />
  }

  const detail = clock.metAt
    ? t('sla.metAt', { when: formatWhen(clock.metAt) })
    : clock.dueAt ? t('sla.dueAt', { when: formatWhen(clock.dueAt) }) : ''
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
      <Box sx={{ flexGrow: 1, minWidth: 0 }}>
        <Typography variant="body2">{t(`sla.clock.${clock.clock}`)}</Typography>
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>{detail}</Typography>
      </Box>
      {chip}
    </Stack>
  )
}
