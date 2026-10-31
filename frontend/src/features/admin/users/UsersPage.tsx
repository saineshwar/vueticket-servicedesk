import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
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
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { orgApi, userApi } from '../../../api/endpoints'
import type { CreateUser } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { Person } from '../../../components/Person'

export function UsersPage() {
  const { t } = useTranslation()
  const [q, setQ] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [creating, setCreating] = useState(false)

  const users = useQuery({
    queryKey: ['users-admin', search, page],
    queryFn: () => userApi.search(search, page + 1, 25),
    placeholderData: keepPreviousData,
  })

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('users.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('users.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setCreating(true)}>{t('users.newUser')}</Button>
      </Stack>

      <Stack component="form" direction="row" spacing={1.5} onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()); setPage(0) }}>
        <TextField id="users-search" label={t('org.searchPeople')} value={q} onChange={(e) => setQ(e.target.value)} sx={{ maxWidth: 360 }} />
        <Button type="submit" variant="outlined">{t('tickets.searchButton')}</Button>
      </Stack>
      <ErrorAlert error={users.error} />

      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t('users.name')}</TableCell>
                <TableCell>{t('users.userName')}</TableCell>
                <TableCell>{t('users.email')}</TableCell>
                <TableCell>{t('users.type')}</TableCell>
                <TableCell>{t('users.homeUnit')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {users.data?.items.map((u) => (
                <TableRow key={u.userId} hover>
                  <TableCell>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                      <Person name={u.displayName} />
                      {!u.isActive && <Chip size="small" label={t('org.inactive')} />}
                    </Stack>
                  </TableCell>
                  <TableCell sx={{ fontFamily: 'Consolas, ui-monospace, monospace', color: 'text.secondary' }}>{u.userName}</TableCell>
                  <TableCell sx={{ color: 'text.secondary' }}>{u.email}</TableCell>
                  <TableCell>
                    <Chip size="small" variant={u.userType === 'Staff' ? 'filled' : 'outlined'}
                      color={u.userType === 'Staff' ? 'primary' : u.userType === 'Customer' ? 'secondary' : 'default'}
                      label={t(`users.types.${u.userType}`, { defaultValue: u.userType })} />
                  </TableCell>
                  <TableCell>{u.homeOrgUnitName ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
        <TablePagination component="div" count={users.data?.total ?? 0} page={page} onPageChange={(_, p) => setPage(p)}
          rowsPerPage={25} rowsPerPageOptions={[25]} />
      </Paper>

      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
    </Stack>
  )
}

function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const units = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree })
  const [form, setForm] = useState<CreateUser>({
    userName: '', email: '', firstName: '', lastName: '', userType: 'Employee', homeOrgUnitId: null, managerUserId: null, password: '',
  })
  const set = <K extends keyof CreateUser>(key: K, value: CreateUser[K]) => setForm((f) => ({ ...f, [key]: value }))

  const save = useMutation({
    mutationFn: () => userApi.create(form),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['users-admin'] })
      onClose()
    },
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{t('users.newUser')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField id="user-first" label={t('setup.firstName')} value={form.firstName ?? ''} onChange={(e) => set('firstName', e.target.value)} />
              <TextField id="user-last" label={t('setup.lastName')} value={form.lastName ?? ''} onChange={(e) => set('lastName', e.target.value)} />
            </Stack>
            <TextField id="user-username" required label={t('users.userName')} value={form.userName} onChange={(e) => set('userName', e.target.value)} autoComplete="off" />
            <TextField id="user-email" required type="email" label={t('users.email')} value={form.email} onChange={(e) => set('email', e.target.value)} autoComplete="off" />
            <TextField id="user-type" select label={t('users.type')} value={form.userType} onChange={(e) => set('userType', e.target.value as CreateUser['userType'])}
              helperText={t('users.typeHelp')}>
              {(['Employee', 'Staff', 'Customer'] as const).map((x) => <MenuItem key={x} value={x}>{t(`users.types.${x}`)}</MenuItem>)}
            </TextField>
            <TextField id="user-unit" select label={t('users.homeUnit')} value={form.homeOrgUnitId ?? ''}
              onChange={(e) => set('homeOrgUnitId', e.target.value === '' ? null : Number(e.target.value))}>
              <MenuItem value="">—</MenuItem>
              {(units.data ?? []).filter((u) => u.isActive).map((u) => (
                <MenuItem key={u.orgUnitId} value={u.orgUnitId} sx={{ pl: 2 + u.depth * 2 }}>{u.name}</MenuItem>
              ))}
            </TextField>
            <TextField id="user-password" required type="password" label={t('setup.password')} value={form.password}
              onChange={(e) => set('password', e.target.value)} helperText={t('setup.passwordHelp')} autoComplete="new-password" />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending || !form.userName.trim() || !form.email.trim() || !form.password}>
            {t('users.create')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
