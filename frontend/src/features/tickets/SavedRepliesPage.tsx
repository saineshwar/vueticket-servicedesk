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
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { cannedApi, orgApi } from '../../api/endpoints'
import type { CannedResponse } from '../../api/types'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { placeholders } from './savedReplyText'

export function SavedRepliesPage() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const replies = useQuery({ queryKey: ['canned'], queryFn: cannedApi.list })
  const [editing, setEditing] = useState<CannedResponse | 'new' | null>(null)
  const remove = useMutation({ mutationFn: cannedApi.remove, onSuccess: () => qc.invalidateQueries({ queryKey: ['canned'] }) })

  return (
    <Stack spacing={2} sx={{ maxWidth: 900 }}>
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h1" component="h1">{t('savedReplies.title')}</Typography>
          <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('savedReplies.intro')}</Typography>
        </Box>
        <Button variant="contained" onClick={() => setEditing('new')}>{t('savedReplies.new')}</Button>
      </Stack>
      <ErrorAlert error={replies.error ?? remove.error} />
      {replies.data?.length === 0 && <Typography sx={{ color: 'text.secondary' }}>{t('savedReplies.empty')}</Typography>}
      {replies.data?.map((r) => (
        <Paper key={r.cannedResponseId} variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1, flexWrap: 'wrap' }}>
            <Typography sx={{ fontWeight: 650, flexGrow: 1 }}>{r.title}</Typography>
            <Chip size="small" variant="outlined" label={r.isPersonal ? t('savedReplies.personal') : r.teamName} />
            {r.canEdit && (
              <>
                <Button size="small" onClick={() => setEditing(r)}>{t('org.edit')}</Button>
                <Button size="small" color="error" disabled={remove.isPending} onClick={() => remove.mutate(r.cannedResponseId)}>{t('org.remove')}</Button>
              </>
            )}
          </Stack>
          <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', color: 'text.secondary' }}>{r.body}</Typography>
        </Paper>
      ))}
      {editing && <SavedReplyDialog reply={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </Stack>
  )
}

function SavedReplyDialog({ reply, onClose }: { reply?: CannedResponse; onClose: () => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const units = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree, enabled: !reply && can('kb.edit') })
  const [title, setTitle] = useState(reply?.title ?? '')
  const [body, setBody] = useState(reply?.body ?? '')
  const [teamId, setTeamId] = useState<number | ''>(reply?.teamId ?? '')

  const save = useMutation({
    mutationFn: () => {
      const request = { title, body, teamId: teamId === '' ? null : Number(teamId) }
      return reply ? cannedApi.update(reply.cannedResponseId, request) : cannedApi.create(request)
    },
    onSuccess: async () => { await qc.invalidateQueries({ queryKey: ['canned'] }); onClose() },
  })

  const teams = (units.data ?? []).filter((u) => u.unitType === 'Team' && u.isActive)

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <DialogTitle>{reply ? t('savedReplies.edit') : t('savedReplies.new')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ErrorAlert error={save.error} />
            <TextField id="saved-title" required label={t('savedReplies.titleLabel')} value={title} onChange={(e) => setTitle(e.target.value)} />
            <TextField id="saved-body" required multiline minRows={5} label={t('savedReplies.body')} value={body} onChange={(e) => setBody(e.target.value)}
              helperText={t('savedReplies.placeholders', { list: placeholders.join('  ') })} />
            {!reply && teams.length > 0 && (
              <TextField id="saved-team" select label={t('savedReplies.shareWith')} value={teamId} onChange={(e) => setTeamId(e.target.value === '' ? '' : Number(e.target.value))}>
                <MenuItem value="">{t('savedReplies.onlyMe')}</MenuItem>
                {teams.map((u) => <MenuItem key={u.orgUnitId} value={u.orgUnitId}>{u.name}</MenuItem>)}
              </TextField>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={!title.trim() || !body.trim() || save.isPending}>{t('common.save')}</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
