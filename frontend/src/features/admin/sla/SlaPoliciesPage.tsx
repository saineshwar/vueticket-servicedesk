import AddOutlined from '@mui/icons-material/AddOutlined'
import DeleteOutline from '@mui/icons-material/DeleteOutlined'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
import FormGroup from '@mui/material/FormGroup'
import IconButton from '@mui/material/IconButton'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
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
import type { EscalationLevel, SlaOptions, SlaPolicy } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { PriorityChip } from '../../tickets/ticketUi'
import { DurationField } from '../../sla/DurationField'
import { formatMinutes } from '../../sla/slaFormat'
import { showEmptyOption } from '../../../components/selectProps'

export function SlaPoliciesPage() {
  const { t } = useTranslation()
  const policies = useQuery({ queryKey: ['sla-policies'], queryFn: slaApi.policies })
  const options = useQuery({ queryKey: ['sla-options'], queryFn: slaApi.options })
  const [editing, setEditing] = useState<{ policy?: SlaPolicy } | null>(null)

  const scope = (p: SlaPolicy) =>
    [p.serviceDeskName ?? t('slaPolicies.allDesks'), p.ticketTypeName ?? t('slaPolicies.allTypes')].join(' · ')
  const escalatesOn = (p: SlaPolicy) =>
    [
      p.escalateOnFirstResponse && t('sla.clock.first_response'),
      p.escalateOnNextResponse && t('sla.clock.next_response'),
      p.escalateOnResolution && t('sla.clock.resolution'),
    ].filter(Boolean).join(', ')

  return (
    <Stack spacing={2} sx={{ maxWidth: 1200 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('slaPolicies.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('slaPolicies.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setEditing({})} disabled={!options.data}>{t('slaPolicies.new')}</Button>
      </Stack>
      <ErrorAlert error={policies.error ?? options.error} />

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t('slaPolicies.title')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('slaPolicies.policy')}</TableCell>
                <TableCell>{t('slaPolicies.priority')}</TableCell>
                <TableCell>{t('sla.clock.first_response')}</TableCell>
                <TableCell>{t('sla.clock.next_response')}</TableCell>
                <TableCell>{t('sla.clock.resolution')}</TableCell>
                <TableCell>{t('slaPolicies.escalation')}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {policies.data?.map((p) => (
                <TableRow key={p.slaPolicyId} sx={{ opacity: p.isActive ? 1 : 0.6 }}>
                  <TableCell sx={{ minWidth: 220 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>
                      {p.name} {!p.isActive && <Chip size="small" label={t('org.inactive')} sx={{ ml: 1 }} />}
                    </Typography>
                    <Typography variant="caption" sx={{ color: 'text.secondary' }}>{scope(p)}</Typography>
                  </TableCell>
                  <TableCell><PriorityChip id={p.priorityId} name={p.priorityName} /></TableCell>
                  <TableCell sx={{ fontVariantNumeric: 'tabular-nums' }}>{formatMinutes(p.firstResponseMinutes)}</TableCell>
                  <TableCell sx={{ fontVariantNumeric: 'tabular-nums' }}>{formatMinutes(p.nextResponseMinutes)}</TableCell>
                  <TableCell sx={{ fontVariantNumeric: 'tabular-nums' }}>{formatMinutes(p.resolutionMinutes)}</TableCell>
                  <TableCell sx={{ minWidth: 220 }}>
                    {escalatesOn(p) ? (
                      <>
                        <Typography variant="body2">{p.levels.map((l) => l.roleName).join(' → ')}</Typography>
                        <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('slaPolicies.when', { clocks: escalatesOn(p).toLowerCase() })}</Typography>
                      </>
                    ) : (
                      <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('slaPolicies.noEscalation')}</Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <Button size="small" onClick={() => setEditing({ policy: p })} disabled={!options.data}>{t('org.edit')}</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      </Paper>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('slaPolicies.matching')}</Typography>

      {editing && options.data && <PolicyDialog policy={editing.policy} options={options.data} onClose={() => setEditing(null)} />}
    </Stack>
  )
}

function PolicyDialog({ policy, options, onClose }: { policy?: SlaPolicy; options: SlaOptions; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(policy?.name ?? '')
  const [deskId, setDeskId] = useState<number | ''>(policy?.serviceDeskId ?? '')
  const [typeId, setTypeId] = useState<number | ''>(policy?.ticketTypeId ?? '')
  const [priorityId, setPriorityId] = useState(policy?.priorityId ?? options.priorities[0]?.id ?? 1)
  const [first, setFirst] = useState(policy?.firstResponseMinutes ?? 240)
  const [next, setNext] = useState(policy?.nextResponseMinutes ?? 480)
  const [resolution, setResolution] = useState(policy?.resolutionMinutes ?? 1440)
  const [reminder, setReminder] = useState(policy?.reminderMinutes ?? 60)
  const [onFirst, setOnFirst] = useState(policy?.escalateOnFirstResponse ?? false)
  const [onNext, setOnNext] = useState(policy?.escalateOnNextResponse ?? false)
  const [onResolution, setOnResolution] = useState(policy?.escalateOnResolution ?? true)
  const [isActive, setIsActive] = useState(policy?.isActive ?? true)
  const [levels, setLevels] = useState<EscalationLevel[]>(policy?.levels ?? [])

  const escalates = onFirst || onNext || onResolution

  const save = useMutation({
    mutationFn: () => slaApi.savePolicy(policy?.slaPolicyId ?? null, {
      name, serviceDeskId: deskId === '' ? null : deskId, ticketTypeId: typeId === '' ? null : typeId, priorityId,
      firstResponseMinutes: first, nextResponseMinutes: next, resolutionMinutes: resolution, reminderMinutes: reminder,
      escalateOnFirstResponse: onFirst, escalateOnNextResponse: onNext, escalateOnResolution: onResolution, isActive,
      levels: levels.map((l, i) => ({ ...l, level: i + 1, windowMinutes: i === levels.length - 1 ? null : l.windowMinutes })),
    }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['sla-policies'] })
      onClose()
    },
  })

  const updateLevel = (index: number, patch: Partial<EscalationLevel>) => setLevels((all) => all.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  const addLevel = () =>
    setLevels((all) => [...all, { level: all.length + 1, roleId: options.roles[0]?.id ?? 0, roleName: null, windowMinutes: 60 }])

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{policy ? t('slaPolicies.edit', { name: policy.name }) : t('slaPolicies.new')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="policy-name" required label={t('slaPolicies.name')} value={name} onChange={(e) => setName(e.target.value)} placeholder="IT Help - Urgent incidents" />

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField id="policy-desk" select slotProps={showEmptyOption} fullWidth label={t('slaPolicies.desk')} value={deskId} onChange={(e) => setDeskId(e.target.value === '' ? '' : Number(e.target.value))}>
                <MenuItem value="">{t('slaPolicies.allDesks')}</MenuItem>
                {options.desks.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
              </TextField>
              <TextField id="policy-type" select slotProps={showEmptyOption} fullWidth label={t('slaPolicies.type')} value={typeId} onChange={(e) => setTypeId(e.target.value === '' ? '' : Number(e.target.value))}>
                <MenuItem value="">{t('slaPolicies.allTypes')}</MenuItem>
                {options.ticketTypes.map((x) => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}
              </TextField>
              <TextField id="policy-priority" select fullWidth label={t('slaPolicies.priority')} value={priorityId} onChange={(e) => setPriorityId(Number(e.target.value))}>
                {options.priorities.map((p) => <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>)}
              </TextField>
            </Stack>

            <Box>
              <Typography variant="subtitle2">{t('slaPolicies.targets')}</Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('slaPolicies.targetsHelp')}</Typography>
              <Stack direction="row" sx={{ gap: 3, flexWrap: 'wrap', mt: 1 }}>
                <DurationField id="policy-first" label={t('sla.clock.first_response')} value={first} onChange={setFirst} />
                <DurationField id="policy-next" label={t('sla.clock.next_response')} value={next} onChange={setNext} helperText={t('slaPolicies.nextHelp')} />
                <DurationField id="policy-resolution" label={t('sla.clock.resolution')} value={resolution} onChange={setResolution} />
                <DurationField id="policy-reminder" label={t('slaPolicies.reminder')} value={reminder} onChange={setReminder} helperText={t('slaPolicies.reminderHelp')} />
              </Stack>
            </Box>

            <Box>
              <Typography variant="subtitle2">{t('slaPolicies.escalateWhen')}</Typography>
              <FormGroup row>
                <FormControlLabel control={<Checkbox checked={onFirst} onChange={(e) => setOnFirst(e.target.checked)} />} label={t('sla.clock.first_response')} />
                <FormControlLabel control={<Checkbox checked={onNext} onChange={(e) => setOnNext(e.target.checked)} />} label={t('sla.clock.next_response')} />
                <FormControlLabel control={<Checkbox checked={onResolution} onChange={(e) => setOnResolution(e.target.checked)} />} label={t('sla.clock.resolution')} />
              </FormGroup>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('slaPolicies.escalateHelp')}</Typography>
            </Box>

            {escalates && (
              <Stack spacing={1.5} role="group" aria-label={t('slaPolicies.levels')}>
                <Typography variant="subtitle2">{t('slaPolicies.levels')}</Typography>
                {levels.map((l, i) => {
                  const last = i === levels.length - 1
                  return (
                    <Paper key={i} variant="outlined" sx={{ p: 1.5 }}>
                      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: { md: 'center' } }}>
                        <Typography sx={{ fontWeight: 600, minWidth: 72, whiteSpace: 'nowrap' }}>{t('slaPolicies.level', { level: i + 1 })}</Typography>
                        <TextField select size="small" label={t('slaPolicies.role')} value={l.roleId} sx={{ minWidth: 220 }}
                          onChange={(e) => updateLevel(i, { roleId: Number(e.target.value) })}>
                          {options.roles.map((r) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
                        </TextField>
                        <Box sx={{ flexGrow: 1 }}>
                          {last ? (
                            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('slaPolicies.finalLevel')}</Typography>
                          ) : (
                            <DurationField id={`policy-level-${i}`} label={t('slaPolicies.window')} value={l.windowMinutes ?? 60}
                              onChange={(m) => updateLevel(i, { windowMinutes: m })} />
                          )}
                        </Box>
                        <IconButton aria-label={t('slaPolicies.removeLevel', { level: i + 1 })} onClick={() => setLevels((all) => all.filter((_, j) => j !== i))}>
                          <DeleteOutline />
                        </IconButton>
                      </Stack>
                    </Paper>
                  )
                })}
                {levels.length < 4 && (
                  <Button startIcon={<AddOutlined />} onClick={addLevel} sx={{ alignSelf: 'flex-start' }}>{t('slaPolicies.addLevel')}</Button>
                )}
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('slaPolicies.levelsHelp')}</Typography>
              </Stack>
            )}

            <FormControlLabel control={<Switch id="policy-active" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />} label={t('org.active')} />
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>{t('slaPolicies.appliesNote')}</Typography>
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
