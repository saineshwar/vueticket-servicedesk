import Autocomplete from '@mui/material/Autocomplete'
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
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { roleApi, userApi } from '../../../api/endpoints'
import type { OrgUnit, UserListItem } from '../../../api/types'
import { ErrorAlert } from '../../../components/ErrorAlert'

function useDebounced<T>(value: T, ms = 250) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return v
}

export function GiveRoleDialog({ unit, onClose }: { unit: OrgUnit; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const term = useDebounced(search)
  const [user, setUser] = useState<UserListItem | null>(null)
  const [roleId, setRoleId] = useState<number | ''>('')
  const [includeDescendants, setIncludeDescendants] = useState(unit.unitType !== 'Team')

  const users = useQuery({ queryKey: ['users', term], queryFn: () => userApi.search(term, 1, 20) })
  const roles = useQuery({ queryKey: ['roles'], queryFn: roleApi.list })

  const assign = useMutation({
    mutationFn: () => roleApi.assign(user!.userId, Number(roleId), unit.orgUnitId, includeDescendants),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['assignments'] })
      await qc.invalidateQueries({ queryKey: ['roles'] })
      onClose()
    },
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); assign.mutate() }}>
        <DialogTitle>{t('org.giveRoleTitle', { name: unit.name })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={assign.error} />
            <Autocomplete
              id="assign-user"
              options={users.data?.items ?? []}
              loading={users.isFetching}
              value={user}
              onChange={(_, value) => setUser(value)}
              inputValue={search}
              onInputChange={(_, value) => setSearch(value)}
              filterOptions={(x) => x}
              getOptionLabel={(u) => `${u.displayName} (${u.userName})`}
              isOptionEqualToValue={(a, b) => a.userId === b.userId}
              renderInput={(params) => <TextField {...params} label={t('org.person')} placeholder={t('org.searchPeople')} />}
            />
            <TextField id="assign-role" select label={t('org.role')} value={roleId} onChange={(e) => setRoleId(Number(e.target.value))}>
              {(roles.data ?? []).map((r) => (
                <MenuItem key={r.roleId} value={r.roleId}>{r.name}</MenuItem>
              ))}
            </TextField>
            <FormControlLabel
              control={<Switch id="assign-subunits" checked={includeDescendants} onChange={(e) => setIncludeDescendants(e.target.checked)} />}
              label={t('org.applyToSubUnits')}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('org.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={!user || roleId === '' || assign.isPending}>
            {t('org.giveRole')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
