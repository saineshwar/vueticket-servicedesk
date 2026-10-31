import Stack from '@mui/material/Stack'
import Chip from '@mui/material/Chip'
import { useTranslation } from 'react-i18next'
import type { TicketStatus } from '../../api/types'
import { dueState, useDueText } from '../sla/slaFormat'

type ChipColor = 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'error' | 'info'

const statusColor: Record<TicketStatus, ChipColor> = {
  New: 'info',
  Open: 'primary',
  InProgress: 'primary',
  AwaitingRequester: 'warning',
  PendingApproval: 'warning',
  Resolved: 'success',
  Closed: 'default',
  Rejected: 'default',
}

export function StatusChip({ status }: { status: TicketStatus }) {
  const { t } = useTranslation()
  return <Chip size="small" color={statusColor[status]} variant={status === 'Closed' ? 'outlined' : 'filled'} label={t(`status.${status}`)} />
}

const priorityColor: Record<number, ChipColor> = { 1: 'error', 2: 'warning', 3: 'default', 4: 'default' }

export function PriorityChip({ id, name }: { id: number; name: string }) {
  // Urgent and High stand out as tinted chips; Medium and Low stay quiet.
  const urgent = id <= 2
  return <Chip size="small" variant={urgent ? 'filled' : 'outlined'} color={priorityColor[id] ?? 'default'} label={name} />
}

/** Queue column: escalation level first, then the nearest SLA target. */
export function SlaChip({ dueAt, paused, escalationLevel, now }: { dueAt: string | null; paused: boolean; escalationLevel: number | null; now: number }) {
  const { t } = useTranslation()
  const dueText = useDueText()
  const chips = []
  if (escalationLevel) chips.push(<Chip key="esc" size="small" color="error" label={t('sla.escalatedLevel', { level: escalationLevel })} />)
  if (paused) chips.push(<Chip key="due" size="small" variant="outlined" label={t('sla.paused')} />)
  else if (dueAt) {
    const state = dueState(dueAt, now)
    chips.push(
      <Chip key="due" size="small" variant="filled"
        color={state === 'overdue' ? 'error' : state === 'soon' ? 'warning' : 'default'} label={dueText(dueAt, now)} />,
    )
  }
  return chips.length ? <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'nowrap' }}>{chips}</Stack> : null
}
