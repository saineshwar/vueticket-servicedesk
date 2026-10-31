import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { slaApi } from '../../../api/endpoints'
import type { AssignmentMode, LookupItem, UnitSettings } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'

const modes: AssignmentMode[] = ['LoadBalanced', 'RoundRobin', 'Manual']

/** Units in tree order with their depth, limited to the units the API returned (the ones this user may configure). */
function treeOrder(units: UnitSettings[]): { unit: UnitSettings; depth: number }[] {
  const ids = new Set(units.map((u) => u.orgUnitId))
  const children = new Map<number | null, UnitSettings[]>()
  for (const u of units) {
    // A unit whose parent isn't listed is shown at the top level.
    const parent = u.parentId !== null && ids.has(u.parentId) ? u.parentId : null
    children.set(parent, [...(children.get(parent) ?? []), u])
  }
  const result: { unit: UnitSettings; depth: number }[] = []
  const walk = (parent: number | null, depth: number) => {
    for (const u of (children.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name))) {
      result.push({ unit: u, depth })
      walk(u.orgUnitId, depth + 1)
    }
  }
  walk(null, 0)
  return result
}

export function UnitSettingsPage() {
  const { t } = useTranslation()
  const units = useQuery({ queryKey: ['unit-settings'], queryFn: slaApi.unitSettings })
  const options = useQuery({ queryKey: ['sla-options'], queryFn: slaApi.options })
  const [editing, setEditing] = useState<UnitSettings | null>(null)

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Box>
        <Typography variant="h1" component="h1">{t('unitSettings.title')}</Typography>
        <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('unitSettings.intro')}</Typography>
      </Box>
      <ErrorAlert error={units.error ?? options.error} />

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t('unitSettings.title')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('unitSettings.unit')}</TableCell>
                <TableCell>{t('unitSettings.hours')}</TableCell>
                <TableCell>{t('unitSettings.assignment')}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {units.data && treeOrder(units.data).map(({ unit: u, depth }) => (
                <TableRow key={u.orgUnitId} sx={{ opacity: u.isActive ? 1 : 0.6 }}>
                  <TableCell sx={{ pl: 2 + depth * 2.5, minWidth: 240 }}>
                    <Typography variant="body2" sx={{ fontWeight: u.unitType === 'Team' ? 400 : 600 }}>{u.name}</Typography>
                    <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t(`unitType.${u.unitType}`)} · {u.code}</Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{u.effectiveCalendarName ?? '—'}</Typography>
                    {u.calendarInherited && <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('unitSettings.inherited')}</Typography>}
                  </TableCell>
                  <TableCell>
                    {u.unitType === 'Team' && u.assignmentMode ? (
                      <>
                        <Typography variant="body2">{t(`unitSettings.mode.${u.assignmentMode}`)}</Typography>
                        {u.assignmentMode !== 'Manual' && (
                          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                            {t('unitSettings.limits', { max: u.maxOpenTickets, alert: u.unassignedAlertMinutes })}
                          </Typography>
                        )}
                      </>
                    ) : null}
                  </TableCell>
                  <TableCell align="right">
                    <Button size="small" onClick={() => setEditing(u)} disabled={!options.data}>{t('org.edit')}</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
        {units.data?.length === 0 && <Typography sx={{ p: 3, color: 'text.secondary' }}>{t('unitSettings.none')}</Typography>}
      </Paper>

      {editing && options.data && <UnitDialog unit={editing} calendars={options.data.calendars} onClose={() => setEditing(null)} />}
    </Stack>
  )
}

function UnitDialog({ unit, calendars, onClose }: { unit: UnitSettings; calendars: LookupItem[]; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const isTeam = unit.unitType === 'Team'
  const [calendarId, setCalendarId] = useState<number | ''>(unit.calendarId ?? '')
  const [mode, setMode] = useState<AssignmentMode>(unit.assignmentMode ?? 'LoadBalanced')
  const [maxOpen, setMaxOpen] = useState(unit.maxOpenTickets ?? 20)
  const [alert, setAlert] = useState(unit.unassignedAlertMinutes ?? 15)

  const save = useMutation({
    mutationFn: () => slaApi.saveUnitSettings(unit.orgUnitId, {
      calendarId: calendarId === '' ? null : calendarId,
      assignmentMode: isTeam ? mode : null,
      maxOpenTickets: isTeam ? maxOpen : null,
      unassignedAlertMinutes: isTeam ? alert : null,
    }),
    onSuccess: async () => {
      // Children inherit the calendar, so refresh the whole list.
      await qc.invalidateQueries({ queryKey: ['unit-settings'] })
      await qc.invalidateQueries({ queryKey: ['calendars'] })
      onClose()
    },
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{t('unitSettings.edit', { name: unit.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="unit-calendar" select label={t('unitSettings.hours')} value={calendarId} disabled={!unit.canEditCalendar}
              onChange={(e) => setCalendarId(e.target.value === '' ? '' : Number(e.target.value))}
              helperText={unit.canEditCalendar ? t('unitSettings.hoursHelp') : t('unitSettings.noCalendarRights')}>
              <MenuItem value="">{unit.parentId === null ? t('unitSettings.useDefault') : t('unitSettings.sameAsParent')}</MenuItem>
              {calendars.map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
            </TextField>

            {isTeam && (
              <>
                <TextField id="unit-mode" select label={t('unitSettings.assignment')} value={mode} disabled={!unit.canEditAssignment}
                  onChange={(e) => setMode(e.target.value as AssignmentMode)}
                  helperText={unit.canEditAssignment ? t(`unitSettings.modeHelp.${mode}`) : t('unitSettings.noRoutingRights')}>
                  {modes.map((m) => <MenuItem key={m} value={m}>{t(`unitSettings.mode.${m}`)}</MenuItem>)}
                </TextField>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                  <TextField id="unit-max-open" type="number" label={t('unitSettings.maxOpen')} value={maxOpen} disabled={!unit.canEditAssignment || mode === 'Manual'}
                    onChange={(e) => setMaxOpen(Number(e.target.value))} helperText={t('unitSettings.maxOpenHelp')}
                    slotProps={{ htmlInput: { min: 1, max: 500 } }} />
                  <TextField id="unit-alert" type="number" label={t('unitSettings.alert')} value={alert} disabled={!unit.canEditAssignment}
                    onChange={(e) => setAlert(Number(e.target.value))} helperText={t('unitSettings.alertHelp')}
                    slotProps={{ htmlInput: { min: 1, max: 1440 } }} />
                </Stack>
              </>
            )}
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('unitSettings.appliesNote')}</Typography>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending}>{t('common.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
