import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

/** API times are UTC without a zone marker. */
export function parseUtc(iso: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`)
}

/** Re-renders every interval so relative times ("due in 12m") stay current. */
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}

/** Compact duration: 45m, 3h 20m, 2d 4h. */
export function formatMinutes(total: number): string {
  const m = Math.max(0, Math.round(total))
  if (m < 60) return `${m}m`
  const days = Math.floor(m / 1440)
  const hours = Math.floor((m % 1440) / 60)
  const minutes = m % 60
  if (days > 0) return hours ? `${days}d ${hours}h` : `${days}d`
  return minutes ? `${hours}h ${minutes}m` : `${hours}h`
}

export type DueState = 'overdue' | 'soon' | 'ok'

/** Within an hour counts as "soon". */
export function dueState(dueAt: string, now: number): DueState {
  const left = (parseUtc(dueAt).getTime() - now) / 60_000
  return left < 0 ? 'overdue' : left <= 60 ? 'soon' : 'ok'
}

export function useDueText() {
  const { t } = useTranslation()
  return (dueAt: string, now: number): string => {
    const left = (parseUtc(dueAt).getTime() - now) / 60_000
    // Round away from zero so a just-missed target never reads "overdue by 0m".
    return left < 0
      ? t('sla.overdueBy', { time: formatMinutes(Math.max(1, Math.ceil(-left))) })
      : t('sla.dueIn', { time: formatMinutes(Math.max(1, Math.ceil(left))) })
  }
}

/** Business-time targets are entered as hours + minutes; stored as minutes. */
export function splitMinutes(total: number): { hours: number; minutes: number } {
  return { hours: Math.floor(total / 60), minutes: total % 60 }
}

export const dayKeys = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'] as const

export function minuteToTime(minute: number): string {
  if (minute >= 1440) return '24:00'
  return `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`
}

export function timeToMinute(value: string): number {
  const [h, m] = value.split(':').map(Number)
  return h * 60 + (m || 0)
}
