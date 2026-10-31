import AddOutlined from '@mui/icons-material/AddOutlined'
import DeleteOutline from '@mui/icons-material/DeleteOutlined'
import Autocomplete from '@mui/material/Autocomplete'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Divider from '@mui/material/Divider'
import FormControlLabel from '@mui/material/FormControlLabel'
import FormGroup from '@mui/material/FormGroup'
import IconButton from '@mui/material/IconButton'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { slaApi } from '../../../api/endpoints'
import type { BusinessCalendar, CalendarHours, Holiday, HolidayList } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { dayKeys, minuteToTime, timeToMinute } from '../../sla/slaFormat'

/** Monday first, the way most people read a working week. */
const weekOrder = [1, 2, 3, 4, 5, 6, 0]

const timeZones: string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone')
  } catch {
    return ['UTC']
  }
})()

type Editing = { kind: 'calendar'; calendar?: BusinessCalendar } | { kind: 'holidays'; list?: HolidayList } | null

export function CalendarsPage() {
  const { t } = useTranslation()
  const calendars = useQuery({ queryKey: ['calendars'], queryFn: slaApi.calendars })
  const lists = useQuery({ queryKey: ['holiday-lists'], queryFn: slaApi.holidayLists })
  const [editing, setEditing] = useState<Editing>(null)
  const listName = (id: number) => lists.data?.find((l) => l.holidayListId === id)?.name ?? ''

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('calendars.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('calendars.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setEditing({ kind: 'calendar' })}>{t('calendars.new')}</Button>
      </Stack>
      <ErrorAlert error={calendars.error ?? lists.error} />

      {calendars.data?.map((c) => (
        <Paper key={c.calendarId} variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'flex-start' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <Typography variant="h2" component="h2">{c.name}</Typography>
                {c.isDefault && <Chip size="small" color="primary" label={t('calendars.default')} />}
                {!c.isActive && <Chip size="small" label={t('org.inactive')} />}
              </Stack>
              <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
                {c.timeZoneId} · {c.is24x7 ? t('calendars.open24x7') : <HoursSummary hours={c.hours} />}
              </Typography>
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {c.holidayListIds.length > 0
                  ? t('calendars.holidaysFrom', { lists: c.holidayListIds.map(listName).join(', ') })
                  : t('calendars.noHolidays')}
                {' · '}
                {c.isDefault ? t('calendars.usedByDefault') : t('calendars.usedBy', { count: c.unitsUsing })}
              </Typography>
            </Box>
            <Button variant="outlined" onClick={() => setEditing({ kind: 'calendar', calendar: c })}>{t('org.edit')}</Button>
          </Stack>
        </Paper>
      ))}

      <Divider sx={{ pt: 1 }} />
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h2" component="h2">{t('calendars.holidayLists')}</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>{t('calendars.holidayListsIntro')}</Typography>
        </Box>
        <Button variant="outlined" onClick={() => setEditing({ kind: 'holidays' })}>{t('calendars.newHolidayList')}</Button>
      </Stack>
      {lists.data?.map((l) => {
        const upcoming = l.holidays.filter((h) => h.date >= new Date().toISOString().slice(0, 10)).slice(0, 3)
        return (
          <Paper key={l.holidayListId} variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'center' } }}>
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }}>{l.name}</Typography>
                <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                  {t('calendars.holidayCount', { count: l.holidays.length })}
                  {upcoming.length > 0 && ` · ${t('calendars.next')}: ${upcoming.map((h) => `${h.name} (${h.date})`).join(', ')}`}
                </Typography>
              </Box>
              <Button variant="outlined" onClick={() => setEditing({ kind: 'holidays', list: l })}>{t('org.edit')}</Button>
            </Stack>
          </Paper>
        )
      })}

      {editing?.kind === 'calendar' && lists.data && (
        <CalendarDialog calendar={editing.calendar} lists={lists.data} onClose={() => setEditing(null)} />
      )}
      {editing?.kind === 'holidays' && <HolidayListDialog list={editing.list} onClose={() => setEditing(null)} />}
    </Stack>
  )
}

function HoursSummary({ hours }: { hours: CalendarHours[] }) {
  const { t } = useTranslation()
  // Group days that share the same hours: "Mon–Fri 09:00–18:00".
  const byDay = weekOrder.map((d) => ({
    day: d,
    text: hours.filter((h) => h.dayOfWeek === d).map((h) => `${minuteToTime(h.startMinute)}–${minuteToTime(h.endMinute)}`).join(', '),
  })).filter((d) => d.text)
  const groups: { from: number; to: number; text: string }[] = []
  for (const d of byDay) {
    const last = groups[groups.length - 1]
    if (last && last.text === d.text && weekOrder.indexOf(d.day) === weekOrder.indexOf(last.to) + 1) last.to = d.day
    else groups.push({ from: d.day, to: d.day, text: d.text })
  }
  const day = (d: number) => t(`calendars.day.${dayKeys[d]}`)
  return <>{groups.map((g) => `${g.from === g.to ? day(g.from) : `${day(g.from)}–${day(g.to)}`} ${g.text}`).join('; ')}</>
}

function CalendarDialog({ calendar, lists, onClose }: { calendar?: BusinessCalendar; lists: HolidayList[]; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(calendar?.name ?? '')
  const [timeZoneId, setTimeZoneId] = useState(calendar?.timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone)
  const [is24x7, setIs24x7] = useState(calendar?.is24x7 ?? false)
  const [isDefault, setIsDefault] = useState(calendar?.isDefault ?? false)
  const [isActive, setIsActive] = useState(calendar?.isActive ?? true)
  const [hours, setHours] = useState<CalendarHours[]>(
    calendar?.hours.length ? calendar.hours : [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startMinute: 540, endMinute: 1080 })),
  )
  const [holidayListIds, setHolidayListIds] = useState<number[]>(calendar?.holidayListIds ?? [])

  const save = useMutation({
    mutationFn: () => slaApi.saveCalendar(calendar?.calendarId ?? null, { name, timeZoneId, is24x7, isDefault, isActive, hours, holidayListIds }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['calendars'] })
      await qc.invalidateQueries({ queryKey: ['unit-settings'] })
      await qc.invalidateQueries({ queryKey: ['sla-options'] })
      onClose()
    },
  })

  const update = (index: number, patch: Partial<CalendarHours>) => setHours((all) => all.map((h, i) => (i === index ? { ...h, ...patch } : h)))

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{calendar ? t('calendars.edit', { name: calendar.name }) : t('calendars.new')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="calendar-name" required label={t('calendars.name')} value={name} onChange={(e) => setName(e.target.value)} placeholder="London office hours" />
            <Autocomplete
              id="calendar-zone"
              options={timeZones}
              value={timeZoneId}
              onChange={(_, v) => v && setTimeZoneId(v)}
              disableClearable
              renderInput={(params) => <TextField {...params} required label={t('calendars.timeZone')} />}
            />
            <FormControlLabel control={<Switch id="calendar-24x7" checked={is24x7} onChange={(e) => setIs24x7(e.target.checked)} />} label={t('calendars.open24x7Label')} />

            {!is24x7 && (
              <Stack spacing={1} role="group" aria-label={t('calendars.workingHours')}>
                <Typography variant="subtitle2">{t('calendars.workingHours')}</Typography>
                {weekOrder.map((day) => {
                  const rows = hours.map((h, i) => ({ h, i })).filter((x) => x.h.dayOfWeek === day)
                  return (
                    <Stack key={day} direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>
                      <Typography variant="body2" sx={{ width: 44 }}>{t(`calendars.day.${dayKeys[day]}`)}</Typography>
                      {rows.length === 0 && <Typography variant="body2" sx={{ color: 'text.secondary', minWidth: 120 }}>{t('calendars.closed')}</Typography>}
                      {rows.map(({ h, i }) => (
                        <Stack key={i} direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                          <TextField type="time" size="small" value={minuteToTime(h.startMinute)}
                            slotProps={{ htmlInput: { 'aria-label': t('calendars.opens', { day: t(`calendars.day.${dayKeys[day]}`) }), step: 300 } }}
                            onChange={(e) => update(i, { startMinute: timeToMinute(e.target.value) })} sx={{ width: 120 }} />
                          <span>–</span>
                          <TextField type="time" size="small" value={minuteToTime(Math.min(h.endMinute, 1439))}
                            slotProps={{ htmlInput: { 'aria-label': t('calendars.closes', { day: t(`calendars.day.${dayKeys[day]}`) }), step: 300 } }}
                            onChange={(e) => update(i, { endMinute: timeToMinute(e.target.value) || 1440 })} sx={{ width: 120 }} />
                          <IconButton size="small" aria-label={t('calendars.removeHours')} onClick={() => setHours((all) => all.filter((_, j) => j !== i))}>
                            <DeleteOutline fontSize="small" />
                          </IconButton>
                        </Stack>
                      ))}
                      <IconButton size="small" aria-label={t('calendars.addHours', { day: t(`calendars.day.${dayKeys[day]}`) })}
                        onClick={() => setHours((all) => [...all, { dayOfWeek: day, startMinute: 540, endMinute: 1080 }])}>
                        <AddOutlined fontSize="small" />
                      </IconButton>
                    </Stack>
                  )
                })}
              </Stack>
            )}

            <Box>
              <Typography variant="subtitle2">{t('calendars.holidayLists')}</Typography>
              {lists.length === 0 ? (
                <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('calendars.noLists')}</Typography>
              ) : (
                <FormGroup>
                  {lists.map((l) => (
                    <FormControlLabel key={l.holidayListId} label={l.name} control={
                      <Checkbox checked={holidayListIds.includes(l.holidayListId)}
                        onChange={(e) => setHolidayListIds((ids) => (e.target.checked ? [...ids, l.holidayListId] : ids.filter((x) => x !== l.holidayListId)))} />
                    } />
                  ))}
                </FormGroup>
              )}
            </Box>

            <FormControlLabel control={<Switch id="calendar-default" checked={isDefault} disabled={calendar?.isDefault}
              onChange={(e) => setIsDefault(e.target.checked)} />} label={t('calendars.makeDefault')} />
            <FormControlLabel control={<Switch id="calendar-active" checked={isActive} disabled={calendar?.isDefault}
              onChange={(e) => setIsActive(e.target.checked)} />} label={t('org.active')} />
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('calendars.appliesNote')}</Typography>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending || !name.trim()}>{t('common.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

function HolidayListDialog({ list, onClose }: { list?: HolidayList; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(list?.name ?? '')
  const [holidays, setHolidays] = useState<Holiday[]>(list?.holidays ?? [])

  const save = useMutation({
    mutationFn: () => slaApi.saveHolidayList(list?.holidayListId ?? null, { name, holidays: holidays.filter((h) => h.date || h.name) }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['holiday-lists'] })
      onClose()
    },
  })

  const update = (index: number, patch: Partial<Holiday>) => setHolidays((all) => all.map((h, i) => (i === index ? { ...h, ...patch } : h)))

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{list ? t('calendars.editHolidayList', { name: list.name }) : t('calendars.newHolidayList')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="holiday-list-name" required label={t('calendars.listName')} value={name} onChange={(e) => setName(e.target.value)} placeholder="UK bank holidays" />
            <Stack spacing={1}>
              {holidays.map((h, i) => (
                <Stack key={i} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <TextField type="date" size="small" value={h.date} onChange={(e) => update(i, { date: e.target.value })}
                    slotProps={{ htmlInput: { 'aria-label': t('calendars.holidayDate') } }} sx={{ width: 170 }} />
                  <TextField size="small" value={h.name} onChange={(e) => update(i, { name: e.target.value })}
                    slotProps={{ htmlInput: { 'aria-label': t('calendars.holidayName') } }} placeholder={t('calendars.holidayName')} sx={{ flexGrow: 1 }} />
                  <IconButton size="small" aria-label={t('calendars.removeHoliday')} onClick={() => setHolidays((all) => all.filter((_, j) => j !== i))}>
                    <DeleteOutline fontSize="small" />
                  </IconButton>
                </Stack>
              ))}
              <Button startIcon={<AddOutlined />} sx={{ alignSelf: 'flex-start' }} onClick={() => setHolidays((all) => [...all, { date: '', name: '' }])}>
                {t('calendars.addHoliday')}
              </Button>
            </Stack>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending || !name.trim()}>{t('common.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
