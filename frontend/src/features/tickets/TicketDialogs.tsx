import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { orgApi, ticketApi } from '../../api/endpoints'
import { ApiError } from '../../api/http'
import type { TicketDetail } from '../../api/types'
import { ErrorAlert } from '../../components/ErrorAlert'

interface Props {
  ticket: TicketDetail
  onClose: () => void
  onDone: (updated: TicketDetail | null) => void
}

export function TransferDialog({ ticket, onClose, onDone }: Props) {
  const { t } = useTranslation()
  const units = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree })
  const teams = (units.data ?? []).filter((u) => u.unitType === 'Team' && u.isActive && u.orgUnitId !== ticket.teamId)
  const [teamId, setTeamId] = useState<number | ''>('')

  const transfer = useMutation({
    mutationFn: () => ticketApi.transfer(ticket.ticketId, Number(teamId)),
    onSuccess: (updated) => onDone(updated),
    // The caller may lose access to a ticket they hand to another team; that's a success, not an error.
    onError: (err) => { if (err instanceof ApiError && err.code === 'transferred_away') onDone(null) },
  })
  const error = transfer.error instanceof ApiError && transfer.error.code === 'transferred_away' ? null : transfer.error

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <form onSubmit={(e) => { e.preventDefault(); transfer.mutate() }}>
        <DialogTitle>{t('ticket.transferTitle', { id: ticket.trackingId })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={error} />
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('ticket.transferHelp', { team: ticket.teamName })}</Typography>
            <TextField id="transfer-team" select required label={t('ticket.newTeam')} value={teamId} onChange={(e) => setTeamId(Number(e.target.value))}>
              {teams.map((u) => <MenuItem key={u.orgUnitId} value={u.orgUnitId}>{u.name} ({u.code})</MenuItem>)}
            </TextField>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={teamId === '' || transfer.isPending}>{t('ticket.transfer')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

export function DeleteDialog({ ticket, onClose, onDone }: Props) {
  const { t } = useTranslation()
  const [reason, setReason] = useState('')
  const remove = useMutation({ mutationFn: () => ticketApi.remove(ticket.ticketId, reason.trim()), onSuccess: onDone })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <form onSubmit={(e) => { e.preventDefault(); remove.mutate() }}>
        <DialogTitle>{t('ticket.deleteTitle', { id: ticket.trackingId })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={remove.error} />
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('ticket.deleteHelp')}</Typography>
            <TextField id="delete-reason" required multiline minRows={2} label={t('ticket.deleteReason')} value={reason}
              onChange={(e) => setReason(e.target.value)} slotProps={{ htmlInput: { maxLength: 500 } }} placeholder={t('ticket.deleteReasonExample')} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" color="error" disabled={!reason.trim() || remove.isPending}>{t('ticket.delete')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
