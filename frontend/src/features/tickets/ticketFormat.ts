import { useTranslation } from 'react-i18next'
import type { TicketHistory } from '../../api/types'

const dateFormat = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** API times are UTC without a zone marker; show them in the viewer's local time. */
export function formatWhen(iso: string): string {
  const value = /[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`
  return dateFormat.format(new Date(value))
}

/** Turns a history row into a readable sentence. */
export function useHistoryText() {
  const { t } = useTranslation()
  return (h: TicketHistory): string => {
    const who = h.userName ?? t('history.system')
    const status = (s: string | null) => (s ? t(`status.${s}`, { defaultValue: s }) : '')
    switch (h.activity) {
      case 'created':
        return t('history.created', { who })
      case 'status':
        return t('history.status', { who, from: status(h.fromValue), to: status(h.toValue) })
      case 'priority':
        return t('history.priority', { who, from: h.fromValue, to: h.toValue })
      case 'assigned':
        return h.toValue ? t('history.assigned', { who, to: h.toValue }) : t('history.unassigned', { who })
      case 'replied':
        return t('history.replied', { who })
      case 'note':
        return t('history.note', { who })
      case 'requester_replied':
        return t('history.requesterReplied', { who })
      case 'reopened':
        return t('history.reopened', { who })
      case 'attached':
        return t('history.attached', { who, files: h.toValue })
      case 'transferred':
        return t('history.transferred', { who, from: h.fromValue, to: h.toValue })
      case 'deleted':
        return t('history.deleted', { who, reason: h.toValue })
      case 'restored':
        return t('history.restored', { who })
      case 'sla_breached':
        return t('history.slaBreached', { clock: t(`sla.clock.${h.toValue}`, { defaultValue: h.toValue ?? '' }).toLowerCase() })
      case 'escalated': {
        // ToValue is "level|role name".
        const [level, role] = (h.toValue ?? '').split('|')
        return t('history.escalated', { level, role })
      }
      case 'escalation_cleared':
        return t('history.escalationCleared')
      default:
        return `${who}: ${h.activity}`
    }
  }
}
