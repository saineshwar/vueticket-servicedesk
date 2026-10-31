import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
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
import { deskApi, orgApi } from '../../../api/endpoints'
import type { Category, DeskAudience, OrgUnit, ServiceDesk } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'

type Editing =
  | { kind: 'desk'; desk?: ServiceDesk }
  | { kind: 'category'; desk: ServiceDesk; category?: Category }
  | null

export function ServiceDesksPage() {
  const { t } = useTranslation()
  const desks = useQuery({ queryKey: ['desks-admin'], queryFn: deskApi.adminList })
  const units = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree })
  const lookups = useQuery({ queryKey: ['lookups'], queryFn: deskApi.lookups })
  const [editing, setEditing] = useState<Editing>(null)

  const teams = (units.data ?? []).filter((u) => u.unitType === 'Team' && u.isActive)
  const priorityName = (id: number) => lookups.data?.priorities.find((p) => p.id === id)?.name ?? id

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('desks.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('desks.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setEditing({ kind: 'desk' })} disabled={teams.length === 0}>
          {t('desks.newDesk')}
        </Button>
      </Stack>
      <ErrorAlert error={desks.error} />
      {units.data && teams.length === 0 && <Typography sx={{ color: 'text.secondary' }}>{t('desks.needTeams')}</Typography>}

      {desks.data?.map((desk) => (
        <Paper key={desk.serviceDeskId} variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'flex-start' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <Typography variant="h2" component="h2">{desk.name}</Typography>
                <Chip size="small" label={desk.code} sx={{ fontFamily: 'Consolas, ui-monospace, monospace' }} />
                <Chip size="small" variant="outlined" label={t(`desks.audience.${desk.audience}`)} />
                {!desk.isActive && <Chip size="small" label={t('org.inactive')} />}
              </Stack>
              <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
                {desk.description ? `${desk.description} · ` : ''}{t('desks.defaultTeamIs', { team: desk.defaultTeamName })}
              </Typography>
            </Box>
            <Stack direction="row" spacing={1}>
              <Button variant="outlined" onClick={() => setEditing({ kind: 'category', desk })}>{t('desks.addCategory')}</Button>
              <Button variant="outlined" onClick={() => setEditing({ kind: 'desk', desk })}>{t('org.edit')}</Button>
            </Stack>
          </Stack>

          {desk.categories.length === 0 ? (
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 2 }}>{t('desks.noCategories')}</Typography>
          ) : (
            <Box sx={{ overflowX: 'auto', mt: 1.5 }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>{t('desks.category')}</TableCell>
                    <TableCell>{t('desks.routesTo')}</TableCell>
                    <TableCell>{t('desks.type')}</TableCell>
                    <TableCell>{t('desks.defaultPriority')}</TableCell>
                    <TableCell />
                  </TableRow>
                </TableHead>
                <TableBody>
                  {desk.categories.map((c) => (
                    <TableRow key={c.categoryId}>
                      <TableCell>
                        {c.name} {!c.isActive && <Chip size="small" label={t('org.inactive')} sx={{ ml: 1 }} />}
                      </TableCell>
                      <TableCell>{c.teamName ?? t('desks.deskDefault', { team: desk.defaultTeamName })}</TableCell>
                      <TableCell>{c.ticketTypeName}</TableCell>
                      <TableCell>{priorityName(c.defaultPriorityId)}</TableCell>
                      <TableCell align="right">
                        <Button size="small" onClick={() => setEditing({ kind: 'category', desk, category: c })}>{t('org.edit')}</Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
        </Paper>
      ))}

      {editing?.kind === 'desk' && <DeskDialog desk={editing.desk} teams={teams} onClose={() => setEditing(null)} />}
      {editing?.kind === 'category' && lookups.data && (
        <CategoryDialog desk={editing.desk} category={editing.category} teams={teams}
          priorities={lookups.data.priorities} types={lookups.data.ticketTypes} onClose={() => setEditing(null)} />
      )}
    </Stack>
  )
}

function DeskDialog({ desk, teams, onClose }: { desk?: ServiceDesk; teams: OrgUnit[]; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(desk?.name ?? '')
  const [code, setCode] = useState(desk?.code ?? '')
  const [description, setDescription] = useState(desk?.description ?? '')
  const [audience, setAudience] = useState<DeskAudience>(desk?.audience ?? 'Employees')
  const [teamId, setTeamId] = useState<number | ''>(desk?.defaultTeamId ?? '')
  const [isActive, setIsActive] = useState(desk?.isActive ?? true)
  const [autoCloseHours, setAutoCloseHours] = useState(desk?.autoCloseHours ?? 48)

  const save = useMutation({
    mutationFn: () => {
      const body = { name, code, description: description || null, audience, defaultTeamId: Number(teamId), isActive, autoCloseHours }
      return desk ? deskApi.update(desk.serviceDeskId, body) : deskApi.create(body)
    },
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['desks-admin'] })
      await qc.invalidateQueries({ queryKey: ['desks-available'] })
      onClose()
    },
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{desk ? t('desks.editDesk', { name: desk.name }) : t('desks.newDesk')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="desk-name" required label={t('desks.name')} value={name} onChange={(e) => setName(e.target.value)} placeholder="IT Help" />
            <TextField id="desk-code" required label={t('desks.code')} value={code} onChange={(e) => setCode(e.target.value.toUpperCase())}
              helperText={t('desks.codeHelp')} />
            <TextField id="desk-description" label={t('desks.description')} value={description} onChange={(e) => setDescription(e.target.value)} />
            <TextField id="desk-audience" select label={t('desks.audienceLabel')} value={audience} onChange={(e) => setAudience(e.target.value as DeskAudience)}>
              {(['Employees', 'Customers', 'Both'] as DeskAudience[]).map((a) => (
                <MenuItem key={a} value={a}>{t(`desks.audience.${a}`)}</MenuItem>
              ))}
            </TextField>
            <TextField id="desk-team" select required label={t('desks.defaultTeam')} value={teamId}
              onChange={(e) => setTeamId(Number(e.target.value))} helperText={t('desks.defaultTeamHelp')}>
              {teams.map((u) => <MenuItem key={u.orgUnitId} value={u.orgUnitId}>{u.name} ({u.code})</MenuItem>)}
            </TextField>
            <TextField id="desk-auto-close" type="number" label={t('desks.autoClose')} value={autoCloseHours}
              onChange={(e) => setAutoCloseHours(Number(e.target.value))} helperText={t('desks.autoCloseHelp')}
              slotProps={{ htmlInput: { min: 0, max: 720 } }} />
            <FormControlLabel control={<Switch id="desk-active" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />} label={t('org.active')} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending || !name.trim() || !code.trim() || teamId === ''}>{t('common.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

function CategoryDialog({
  desk, category, teams, priorities, types, onClose,
}: {
  desk: ServiceDesk
  category?: Category
  teams: OrgUnit[]
  priorities: { id: number; name: string }[]
  types: { id: number; name: string }[]
  onClose: () => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(category?.name ?? '')
  const [description, setDescription] = useState(category?.description ?? '')
  const [teamId, setTeamId] = useState<number | ''>(category?.teamId ?? '')
  const [typeId, setTypeId] = useState(category?.ticketTypeId ?? 1)
  const [priorityId, setPriorityId] = useState(category?.defaultPriorityId ?? 3)
  const [isActive, setIsActive] = useState(category?.isActive ?? true)

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name, description: description || null, teamId: teamId === '' ? null : Number(teamId),
        ticketTypeId: typeId, defaultPriorityId: priorityId, sortOrder: category?.sortOrder ?? desk.categories.length + 1, isActive,
      }
      return category ? deskApi.updateCategory(category.categoryId, body) : deskApi.createCategory(desk.serviceDeskId, body)
    },
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['desks-admin'] })
      await qc.invalidateQueries({ queryKey: ['desks-available'] })
      onClose()
    },
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{category ? t('desks.editCategory', { name: category.name }) : t('desks.addCategoryTo', { desk: desk.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="category-name" required label={t('desks.category')} value={name} onChange={(e) => setName(e.target.value)} placeholder="Network or VPN" />
            <TextField id="category-description" label={t('desks.description')} value={description} onChange={(e) => setDescription(e.target.value)}
              helperText={t('desks.categoryDescriptionHelp')} />
            <TextField id="category-team" select label={t('desks.routesTo')} value={teamId} onChange={(e) => setTeamId(e.target.value === '' ? '' : Number(e.target.value))}>
              <MenuItem value="">{t('desks.deskDefault', { team: desk.defaultTeamName })}</MenuItem>
              {teams.map((u) => <MenuItem key={u.orgUnitId} value={u.orgUnitId}>{u.name} ({u.code})</MenuItem>)}
            </TextField>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField id="category-type" select label={t('desks.type')} value={typeId} onChange={(e) => setTypeId(Number(e.target.value))}>
                {types.map((x) => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}
              </TextField>
              <TextField id="category-priority" select label={t('desks.defaultPriority')} value={priorityId} onChange={(e) => setPriorityId(Number(e.target.value))}>
                {priorities.map((p) => <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>)}
              </TextField>
            </Stack>
            <FormControlLabel control={<Switch id="category-active" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />} label={t('org.active')} />
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
