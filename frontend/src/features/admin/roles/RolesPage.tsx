import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { roleApi } from '../../../api/endpoints'
import type { Permission, Role } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'

type EditorState = { mode: 'create' | 'edit'; role?: Role; seed?: Role } | null

export function RolesPage() {
  const { t } = useTranslation()
  const roles = useQuery({ queryKey: ['roles'], queryFn: roleApi.list })
  const catalog = useQuery({ queryKey: ['permissions'], queryFn: roleApi.permissions })
  const [editor, setEditor] = useState<EditorState>(null)

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('roles.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('roles.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setEditor({ mode: 'create' })}>{t('roles.newRole')}</Button>
      </Stack>
      <ErrorAlert error={roles.error ?? catalog.error} />

      <Stack spacing={1}>
        {roles.data?.map((role) => (
          <Paper key={role.roleId} variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'center' } }}>
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <Typography sx={{ fontWeight: 650 }}>{role.name}</Typography>
                  {role.isSystem && <Chip size="small" label={t('roles.system')} />}
                </Stack>
                {role.description && (
                  <Typography variant="body2" sx={{ color: 'text.secondary' }}>{role.description}</Typography>
                )}
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  {t('roles.permissionCount', { count: role.permissions.length })} · {t('roles.assignedCount', { count: role.assignmentCount })}
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button variant="outlined" onClick={() => setEditor({ mode: 'create', seed: role })}>{t('roles.copy')}</Button>
                <Button variant="outlined" disabled={role.isSystem} onClick={() => setEditor({ mode: 'edit', role })}>
                  {t('org.edit')}
                </Button>
              </Stack>
            </Stack>
          </Paper>
        ))}
      </Stack>

      {editor && catalog.data && <RoleEditor state={editor} catalog={catalog.data} onClose={() => setEditor(null)} />}
    </Stack>
  )
}

function RoleEditor({ state, catalog, onClose }: { state: NonNullable<EditorState>; catalog: Permission[]; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const source = state.role ?? state.seed
  const [name, setName] = useState(state.role?.name ?? (state.seed ? t('roles.copyOf', { name: state.seed.name }) : ''))
  const [description, setDescription] = useState(source?.description ?? '')
  const [selected, setSelected] = useState<Set<string>>(new Set(source?.permissions ?? []))
  const [confirmDelete, setConfirmDelete] = useState(false)

  const groups = useMemo(() => {
    const map = new Map<string, Permission[]>()
    catalog.forEach((p) => map.set(p.group, [...(map.get(p.group) ?? []), p]))
    return [...map.entries()]
  }, [catalog])

  const done = async () => {
    await qc.invalidateQueries({ queryKey: ['roles'] })
    onClose()
  }

  const save = useMutation({
    mutationFn: () =>
      state.mode === 'edit' && state.role
        ? roleApi.update(state.role.roleId, name, description || null, [...selected])
        : roleApi.create(name, description || null, [...selected]),
    onSuccess: done,
  })
  const remove = useMutation({ mutationFn: () => roleApi.remove(state.role!.roleId), onSuccess: done })

  const toggle = (key: string, on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (on) next.add(key)
      else next.delete(key)
      return next
    })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md" scroll="paper">
      <DialogTitle>{state.mode === 'edit' ? t('roles.editTitle') : t('roles.createTitle')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <ErrorAlert error={save.error ?? remove.error} />
          <TextField id="role-name" label={t('roles.name')} value={name} onChange={(e) => setName(e.target.value)} required />
          <TextField id="role-description" label={t('roles.description')} value={description} onChange={(e) => setDescription(e.target.value)} multiline minRows={2} />
          <Typography variant="h3" component="h3">{t('roles.permissions')}</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2, alignItems: 'start' }}>
            {groups.map(([group, perms]) => {
              const all = perms.every((p) => selected.has(p.key))
              const some = perms.some((p) => selected.has(p.key))
              return (
                <Paper key={group} variant="outlined" sx={{ p: 1.5, minWidth: 0 }}>
                  <FormControlLabel
                    control={
                      <Checkbox
                        checked={all}
                        indeterminate={some && !all}
                        onChange={(e) => perms.forEach((p) => toggle(p.key, e.target.checked))}
                      />
                    }
                    label={<Typography sx={{ fontWeight: 650 }}>{group}</Typography>}
                  />
                  <Stack sx={{ pl: 3 }}>
                    {perms.map((p) => (
                      <FormControlLabel
                        key={p.key}
                        control={<Checkbox size="small" id={`perm-${p.key}`} checked={selected.has(p.key)} onChange={(e) => toggle(p.key, e.target.checked)} />}
                        label={<Typography variant="body2">{p.description}</Typography>}
                      />
                    ))}
                  </Stack>
                </Paper>
              )
            })}
          </Box>
          {confirmDelete && state.role && (
            <Alert
              severity="warning"
              action={
                <Button color="inherit" size="small" disabled={remove.isPending} onClick={() => remove.mutate()}>
                  {t('roles.delete')}
                </Button>
              }
            >
              {t('roles.confirmDelete', { name: state.role.name })}
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {state.mode === 'edit' && (
          <Button color="error" onClick={() => setConfirmDelete(true)} sx={{ mr: 'auto' }}>{t('roles.delete')}</Button>
        )}
        <Button onClick={onClose}>{t('roles.cancel')}</Button>
        <Button variant="contained" disabled={save.isPending || !name.trim() || selected.size === 0} onClick={() => save.mutate()}>
          {t('roles.save')}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
