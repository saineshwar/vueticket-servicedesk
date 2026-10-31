import Alert from '@mui/material/Alert'
import Autocomplete from '@mui/material/Autocomplete'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { deskApi, ticketApi, userApi } from '../../api/endpoints'
import type { UserListItem } from '../../api/types'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { FilePicker } from '../../components/FilePicker'

export function NewTicketPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const desks = useQuery({ queryKey: ['desks-available'], queryFn: deskApi.available })
  const lookups = useQuery({ queryKey: ['lookups'], queryFn: deskApi.lookups })

  const [deskId, setDeskId] = useState<number | null>(null)
  const [categoryId, setCategoryId] = useState<number | ''>('')
  // Null means "use the category's default priority"; picking a category resets it.
  const [priorityOverride, setPriorityOverride] = useState<number | null>(null)
  const [subject, setSubject] = useState('')
  const [description, setDescription] = useState('')
  const [requester, setRequester] = useState<UserListItem | null>(null)
  const [files, setFiles] = useState<File[]>([])
  const [search, setSearch] = useState('')

  const canRaiseForOthers = can('ticket.create.onbehalf')
  const people = useQuery({
    queryKey: ['users', search],
    queryFn: () => userApi.search(search, 1, 20),
    enabled: canRaiseForOthers,
  })

  const desk = desks.data?.find((d) => d.serviceDeskId === deskId) ?? (desks.data?.length === 1 ? desks.data[0] : undefined)
  const category = desk?.categories.find((c) => c.categoryId === categoryId)
  const priorityId: number | '' = priorityOverride ?? category?.defaultPriorityId ?? ''

  const create = useMutation({
    mutationFn: () =>
      ticketApi.create({
        serviceDeskId: desk!.serviceDeskId,
        categoryId: Number(categoryId),
        subject: subject.trim(),
        description: description.trim(),
        priorityId: priorityId === '' ? null : Number(priorityId),
        requesterUserId: requester?.userId ?? null,
      }, files),
    onSuccess: (ticket) => navigate(`/tickets/${ticket.ticketId}`, { state: { created: ticket.trackingId } }),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate()
  }

  if (desks.isLoading) return <CircularProgress aria-label={t('app.loading')} />

  return (
    <Stack spacing={2} sx={{ maxWidth: 760 }}>
      <div>
        <Typography variant="h1" component="h1">{t('newTicket.title')}</Typography>
        <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('newTicket.intro')}</Typography>
      </div>
      <ErrorAlert error={desks.error} />
      {desks.data?.length === 0 && <Alert severity="info">{t('newTicket.noDesks')}</Alert>}

      {desks.data && desks.data.length > 1 && (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
          {desks.data.map((d) => {
            const selected = desk?.serviceDeskId === d.serviceDeskId
            return (
              <Paper
                key={d.serviceDeskId}
                variant="outlined"
                component="button"
                type="button"
                onClick={() => { setDeskId(d.serviceDeskId); setCategoryId('') }}
                aria-pressed={selected}
                sx={{
                  p: 2, textAlign: 'left', cursor: 'pointer', font: 'inherit', color: 'inherit',
                  borderColor: selected ? 'primary.main' : 'divider', borderWidth: selected ? 2 : 1,
                  bgcolor: 'background.paper',
                }}
              >
                <Typography sx={{ fontWeight: 650 }}>{d.name}</Typography>
                {d.description && <Typography variant="body2" sx={{ color: 'text.secondary' }}>{d.description}</Typography>}
              </Paper>
            )
          })}
        </Box>
      )}

      {desk && (
        <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
          <Stack component="form" spacing={2} onSubmit={submit} noValidate>
            <Typography variant="h2" component="h2">{desk.name}</Typography>
            <ErrorAlert error={create.error} />

            {canRaiseForOthers && (
              <Autocomplete
                id="ticket-requester"
                options={people.data?.items ?? []}
                value={requester}
                onChange={(_, value) => setRequester(value)}
                inputValue={search}
                onInputChange={(_, value) => setSearch(value)}
                filterOptions={(x) => x}
                getOptionLabel={(u) => `${u.displayName} (${u.email})`}
                isOptionEqualToValue={(a, b) => a.userId === b.userId}
                renderInput={(params) => (
                  <TextField {...params} label={t('newTicket.requester')} helperText={t('newTicket.requesterHelp')} />
                )}
              />
            )}

            <TextField id="ticket-category" select required label={t('newTicket.category')} value={categoryId}
              onChange={(e) => { setCategoryId(Number(e.target.value)); setPriorityOverride(null) }}
              helperText={category?.description ?? ' '}>
              {desk.categories.map((c) => (
                <MenuItem key={c.categoryId} value={c.categoryId}>{c.name}</MenuItem>
              ))}
            </TextField>

            <TextField id="ticket-subject" required label={t('newTicket.subject')} value={subject}
              onChange={(e) => setSubject(e.target.value)} slotProps={{ htmlInput: { maxLength: 200 } }} />

            <TextField id="ticket-description" required multiline minRows={6} label={t('newTicket.description')}
              value={description} onChange={(e) => setDescription(e.target.value)} helperText={t('newTicket.descriptionHelp')} />

            <FilePicker files={files} onChange={setFiles} disabled={create.isPending} />

            <TextField id="ticket-priority" select label={t('newTicket.priority')} value={priorityId}
              onChange={(e) => setPriorityOverride(Number(e.target.value))} disabled={!category} sx={{ maxWidth: 240 }}>
              {(lookups.data?.priorities ?? []).map((p) => (
                <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>
              ))}
            </TextField>

            <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
              <Button onClick={() => navigate(-1)}>{t('common.cancel')}</Button>
              <Button type="submit" variant="contained"
                disabled={create.isPending || categoryId === '' || !subject.trim() || !description.trim()}>
                {t('newTicket.submit')}
              </Button>
            </Stack>
          </Stack>
        </Paper>
      )}
    </Stack>
  )
}
