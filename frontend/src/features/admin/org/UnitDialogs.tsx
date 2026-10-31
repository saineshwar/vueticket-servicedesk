import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import TextField from '@mui/material/TextField'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { orgApi } from '../../../api/endpoints'
import type { OrgUnit, OrgUnitType } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { childTypesFor, subtreeIds, unitTypeOrder } from './orgTree'

interface DialogProps {
  onClose: (changedId?: number) => void
}

export function AddUnitDialog({ parent, onClose }: DialogProps & { parent: OrgUnit }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const types = childTypesFor(parent.unitType)
  const [unitType, setUnitType] = useState<OrgUnitType>(types[0])
  const [name, setName] = useState('')
  const [code, setCode] = useState('')

  const create = useMutation({
    mutationFn: () => orgApi.create(parent.orgUnitId, unitType, name, code),
    onSuccess: async (unit) => {
      await qc.invalidateQueries({ queryKey: ['org-units'] })
      onClose(unit.orgUnitId)
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate()
  }

  return (
    <Dialog open onClose={() => onClose()} fullWidth maxWidth="xs">
      <form onSubmit={submit}>
        <DialogTitle>{t('org.addTitle', { parent: parent.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={create.error} />
            <TextField id="unit-type" select label={t('org.type')} value={unitType} onChange={(e) => setUnitType(e.target.value as OrgUnitType)}>
              {types.map((type) => (
                <MenuItem key={type} value={type}>{t(`unitType.${type}`)}</MenuItem>
              ))}
            </TextField>
            <TextField id="unit-name" label={t('org.name')} value={name} onChange={(e) => setName(e.target.value)} autoFocus required />
            <TextField
              id="unit-code"
              label={t('org.code')}
              value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())}
              helperText="For example IT-NET"
              required
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => onClose()}>{t('org.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={create.isPending || !name.trim() || !code.trim()}>
            {t('org.create')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

export function EditUnitDialog({ unit, onClose }: DialogProps & { unit: OrgUnit }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [name, setName] = useState(unit.name)
  const [code, setCode] = useState(unit.code)
  const [isActive, setIsActive] = useState(unit.isActive)

  const save = useMutation({
    mutationFn: () => orgApi.update(unit.orgUnitId, name, code, isActive),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['org-units'] })
      onClose(unit.orgUnitId)
    },
  })

  return (
    <Dialog open onClose={() => onClose()} fullWidth maxWidth="xs">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{t('org.editTitle', { name: unit.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="edit-name" label={t('org.name')} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField id="edit-code" label={t('org.code')} value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} required />
            {unit.parentId != null && (
              <FormControlLabel
                control={<Switch id="edit-active" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
                label={t('org.active')}
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => onClose()}>{t('org.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending}>{t('org.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

export function MoveUnitDialog({ unit, units, onClose }: DialogProps & { unit: OrgUnit; units: OrgUnit[] }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const blocked = subtreeIds(units, unit.orgUnitId)
  const rank = (type: OrgUnitType) => unitTypeOrder.indexOf(type)
  const candidates = units.filter(
    (u) => !blocked.has(u.orgUnitId) && u.unitType !== 'Team' && rank(u.unitType) < rank(unit.unitType),
  )
  const [parentId, setParentId] = useState<number | ''>(unit.parentId ?? '')

  const move = useMutation({
    mutationFn: () => orgApi.move(unit.orgUnitId, Number(parentId)),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['org-units'] })
      onClose(unit.orgUnitId)
    },
  })

  return (
    <Dialog open onClose={() => onClose()} fullWidth maxWidth="xs">
      <form onSubmit={(e) => { e.preventDefault(); move.mutate() }}>
        <DialogTitle>{t('org.moveTitle', { name: unit.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={move.error} />
            <TextField id="move-parent" select label={t('org.newParent')} value={parentId} onChange={(e) => setParentId(Number(e.target.value))}>
              {candidates.map((u) => (
                <MenuItem key={u.orgUnitId} value={u.orgUnitId} sx={{ pl: 2 + u.depth * 2 }}>
                  {u.name} · {t(`unitType.${u.unitType}`)}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => onClose()}>{t('org.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={move.isPending || parentId === '' || parentId === unit.parentId}>
            {t('org.move')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
