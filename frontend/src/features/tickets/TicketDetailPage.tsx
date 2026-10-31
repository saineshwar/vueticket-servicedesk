import ArrowBack from '@mui/icons-material/ArrowBack'
import LockOutlined from '@mui/icons-material/LockOutlined'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Divider from '@mui/material/Divider'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Tab from '@mui/material/Tab'
import Tabs from '@mui/material/Tabs'
import TextField from '@mui/material/TextField'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router'
import { deskApi, ticketApi } from '../../api/endpoints'
import type { Attachment, TicketComment, TicketDetail } from '../../api/types'
import { ErrorAlert } from '../../components/ErrorAlert'
import { FilePicker } from '../../components/FilePicker'
import { AttachmentList } from './AttachmentList'
import { SavedRepliesMenu } from './SavedRepliesMenu'
import { EscalationBanner, SlaPanel } from './SlaPanel'
import { DeleteDialog, TransferDialog } from './TicketDialogs'
import { formatWhen, useHistoryText } from './ticketFormat'
import { PriorityChip, StatusChip } from './ticketUi'

export function TicketDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams()
  const ticketId = Number(id)
  const location = useLocation()
  const created = (location.state as { created?: string } | null)?.created
  const qc = useQueryClient()

  const ticket = useQuery({ queryKey: ['ticket', ticketId], queryFn: () => ticketApi.get(ticketId), enabled: Number.isFinite(ticketId) })

  // Every action returns the updated ticket; store it so the page refreshes without a second request.
  const onUpdated = (updated: TicketDetail) => {
    qc.setQueryData(['ticket', ticketId], updated)
    void qc.invalidateQueries({ queryKey: ['tickets'] })
  }

  if (ticket.isLoading) return <CircularProgress aria-label={t('app.loading')} />
  if (ticket.error || !ticket.data) {
    return (
      <Stack spacing={2} sx={{ maxWidth: 640 }}>
        <ErrorAlert error={ticket.error} />
        <Button component={RouterLink} to="/" startIcon={<ArrowBack />} sx={{ alignSelf: 'flex-start' }}>{t('ticket.back')}</Button>
      </Stack>
    )
  }

  const d = ticket.data
  return (
    <Stack spacing={2}>
      <Button component={RouterLink} to={d.viewerIsSupport ? '/tickets' : '/requests'} startIcon={<ArrowBack />} sx={{ alignSelf: 'flex-start' }}>
        {d.viewerIsSupport ? t('ticket.backToQueues') : t('ticket.backToRequests')}
      </Button>
      {created && <Alert severity="success">{t('ticket.created', { id: created })}</Alert>}
      {d.isDeleted && <DeletedBanner ticket={d} onUpdated={onUpdated} />}
      {d.escalation && <EscalationBanner escalation={d.escalation} />}

      <div>
        <Typography variant="caption" sx={{ color: 'text.secondary', fontFamily: 'Consolas, ui-monospace, monospace' }}>
          {d.trackingId} · {d.serviceDeskName} · {d.categoryName}
        </Typography>
        <Typography variant="h1" component="h1" sx={{ mt: 0.5, overflowWrap: 'anywhere' }}>{d.subject}</Typography>
        <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
          <StatusChip status={d.status} />
          <PriorityChip id={d.priorityId} name={d.priorityName} />
        </Stack>
      </div>

      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={2} sx={{ alignItems: 'flex-start' }}>
        <Stack spacing={2} sx={{ flexGrow: 1, minWidth: 0, width: '100%' }}>
          <Conversation ticket={d} />
          <ReplyBox ticket={d} onUpdated={onUpdated} />
        </Stack>
        <Stack spacing={2} sx={{ width: { xs: '100%', lg: 340 }, flexShrink: 0 }}>
          {d.sla && <SlaPanel sla={d.sla} />}
          <SidePanel ticket={d} onUpdated={onUpdated} />
        </Stack>
      </Stack>
    </Stack>
  )
}

function Conversation({ ticket }: { ticket: TicketDetail }) {
  const { t } = useTranslation()
  const opening: TicketComment = {
    ticketCommentId: 0,
    authorUserId: ticket.requesterUserId,
    authorName: ticket.requesterName,
    authorIsSupport: false,
    isInternal: false,
    body: ticket.description,
    createdAt: ticket.createdAt,
  }
  // Files sent with a reply sit under that reply; files added to the ticket itself sit under the opening message.
  const filesFor = (c: TicketComment) =>
    ticket.attachments.filter((a) => (c.ticketCommentId === 0 ? a.ticketCommentId === null : a.ticketCommentId === c.ticketCommentId))
  return (
    <Stack spacing={1.5} component="section" aria-label={t('ticket.conversation')}>
      {[opening, ...ticket.comments].map((c) => <CommentCard key={c.ticketCommentId} comment={c} attachments={filesFor(c)} />)}
    </Stack>
  )
}

function DeletedBanner({ ticket, onUpdated }: { ticket: TicketDetail; onUpdated: (t: TicketDetail) => void }) {
  const { t } = useTranslation()
  const restore = useMutation({ mutationFn: () => ticketApi.restore(ticket.ticketId), onSuccess: onUpdated })
  return (
    <Alert
      severity="warning"
      action={ticket.actions.canRestore && (
        <Button color="inherit" size="small" disabled={restore.isPending} onClick={() => restore.mutate()}>{t('ticket.restore')}</Button>
      )}
    >
      {t('ticket.deletedNotice', { when: ticket.deletedAt ? formatWhen(ticket.deletedAt) : '' })}
      {ticket.deleteReason && <> {t('ticket.deletedReason', { reason: ticket.deleteReason })}</>}
    </Alert>
  )
}

function CommentCard({ comment, attachments }: { comment: TicketComment; attachments: Attachment[] }) {
  const { t } = useTranslation()
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        borderLeftWidth: 4,
        borderLeftColor: comment.isInternal ? 'warning.main' : comment.authorIsSupport ? 'primary.main' : 'divider',
        bgcolor: comment.isInternal ? 'action.hover' : 'background.paper',
      }}
    >
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <Typography sx={{ fontWeight: 650 }}>{comment.authorName}</Typography>
        {comment.authorIsSupport && !comment.isInternal && <Typography variant="caption" sx={{ color: 'primary.main' }}>{t('ticket.support')}</Typography>}
        {comment.isInternal && (
          <Typography variant="caption" sx={{ color: 'warning.dark', display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
            <LockOutlined sx={{ fontSize: 14 }} /> {t('ticket.internalNote')}
          </Typography>
        )}
        <Typography variant="caption" sx={{ color: 'text.secondary', ml: 'auto !important' }}>{formatWhen(comment.createdAt)}</Typography>
      </Stack>
      {/* Plain text, rendered as text: never as HTML. */}
      <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{comment.body}</Typography>
      <AttachmentList attachments={attachments} />
    </Paper>
  )
}

function ReplyBox({ ticket, onUpdated }: { ticket: TicketDetail; onUpdated: (t: TicketDetail) => void }) {
  const { t } = useTranslation()
  const { actions } = ticket
  const [mode, setMode] = useState<'reply' | 'note'>(actions.canReply ? 'reply' : 'note')
  const [body, setBody] = useState('')
  const [newStatus, setNewStatus] = useState('')
  const [files, setFiles] = useState<File[]>([])

  const send = useMutation({
    mutationFn: (sent: string) => ticketApi.comment(ticket.ticketId, sent, mode === 'note', newStatus || null, files),
    // Clear only what was sent, so text typed while the request was in flight is never lost.
    onSuccess: (updated, sent) => {
      setBody((current) => (current.trim() === sent ? '' : current))
      setNewStatus('')
      setFiles([])
      onUpdated(updated)
    },
  })

  if (!actions.canReply && !actions.canAddNote) {
    return ticket.status === 'Closed'
      ? <Alert severity="info">{t('ticket.closedNotice')}</Alert>
      : null
  }

  const isNote = mode === 'note'
  return (
    <Paper variant="outlined" sx={{ p: 2 }} component="form" onSubmit={(e) => { e.preventDefault(); send.mutate(body.trim()) }}>
      <Stack spacing={1.5}>
        {actions.canReply && actions.canAddNote && (
          <ToggleButtonGroup exclusive size="small" value={mode} onChange={(_, v) => v && setMode(v)} aria-label={t('ticket.replyType')}>
            <ToggleButton value="reply">{t('ticket.publicReply')}</ToggleButton>
            <ToggleButton value="note">{t('ticket.internalNote')}</ToggleButton>
          </ToggleButtonGroup>
        )}
        <ErrorAlert error={send.error} />
        {ticket.viewerIsSupport && !isNote && (
          <Box><SavedRepliesMenu ticket={ticket} onInsert={(text) => setBody((current) => (current.trim() ? `${current}\n\n${text}` : text))} /></Box>
        )}
        <TextField
          id="ticket-reply"
          multiline
          minRows={4}
          label={isNote ? t('ticket.notePlaceholder') : ticket.viewerIsSupport ? t('ticket.replyPlaceholder') : t('ticket.requesterReplyPlaceholder')}
          helperText={isNote ? t('ticket.noteHelp') : ' '}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          slotProps={{ htmlInput: { readOnly: send.isPending } }}
          sx={isNote ? { '& .MuiOutlinedInput-root': { bgcolor: 'action.hover' } } : undefined}
        />
        {actions.canAttach && <FilePicker files={files} onChange={setFiles} disabled={send.isPending} />}
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { sm: 'center' } }}>
          {actions.canChangeStatus && actions.allowedStatuses.length > 0 && (
            <TextField id="reply-status" select label={t('ticket.setStatus')} value={newStatus}
              onChange={(e) => setNewStatus(e.target.value)} sx={{ maxWidth: { sm: 240 } }}>
              <MenuItem value="">{t('ticket.keepStatus')}</MenuItem>
              {actions.allowedStatuses.map((s) => <MenuItem key={s} value={s}>{t(`status.${s}`)}</MenuItem>)}
            </TextField>
          )}
          <Box sx={{ flexGrow: 1 }} />
          <Button type="submit" variant="contained" color={isNote ? 'warning' : 'primary'} disabled={!body.trim() || send.isPending}>
            {isNote ? t('ticket.addNote') : t('ticket.send')}
          </Button>
        </Stack>
      </Stack>
    </Paper>
  )
}

function SidePanel({ ticket, onUpdated }: { ticket: TicketDetail; onUpdated: (t: TicketDetail) => void }) {
  const { t } = useTranslation()
  const historyText = useHistoryText()
  const [tab, setTab] = useState<'details' | 'history'>('details')
  const { actions } = ticket

  const lookups = useQuery({ queryKey: ['lookups'], queryFn: deskApi.lookups, enabled: actions.canChangePriority })
  const assignable = useQuery({
    queryKey: ['assignable', ticket.ticketId],
    queryFn: () => ticketApi.assignable(ticket.ticketId),
    enabled: actions.canAssign,
  })

  const navigate = useNavigate()
  const [dialog, setDialog] = useState<'transfer' | 'delete' | null>(null)
  const afterDialog = (updated: TicketDetail | null) => {
    setDialog(null)
    if (updated) onUpdated(updated)
    else navigate('/tickets', { state: { notice: t('ticket.transferredAway') } })
  }

  const status = useMutation({ mutationFn: (s: string) => ticketApi.status(ticket.ticketId, s), onSuccess: onUpdated })
  const priority = useMutation({ mutationFn: (p: number) => ticketApi.priority(ticket.ticketId, p), onSuccess: onUpdated })
  const assign = useMutation({ mutationFn: (u: number | null) => ticketApi.assign(ticket.ticketId, u), onSuccess: onUpdated })

  const row = (label: string, value: ReactNode) => (
    <Stack direction="row" spacing={1} sx={{ py: 0.75 }}>
      <Typography variant="body2" sx={{ color: 'text.secondary', width: 110, flexShrink: 0 }}>{label}</Typography>
      <Box sx={{ minWidth: 0, flexGrow: 1, overflowWrap: 'anywhere' }}>{value}</Box>
    </Stack>
  )

  return (
    <Paper variant="outlined">
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="fullWidth">
        <Tab value="details" label={t('ticket.details')} />
        <Tab value="history" label={t('ticket.history')} />
      </Tabs>
      <Divider />
      {tab === 'details' ? (
        <Stack spacing={1.5} sx={{ p: 2 }}>
          <ErrorAlert error={status.error ?? priority.error ?? assign.error} />

          {actions.canChangeStatus && actions.allowedStatuses.length > 0 && (
            <TextField id="side-status" select label={t('ticket.status')} value={ticket.status}
              onChange={(e) => { if (e.target.value !== ticket.status) status.mutate(e.target.value) }} disabled={status.isPending}>
              {/* The current status is shown but cannot be "changed" to itself. */}
              <MenuItem value={ticket.status} disabled>{t(`status.${ticket.status}`)}</MenuItem>
              {actions.allowedStatuses.map((s) => <MenuItem key={s} value={s}>{t(`status.${s}`)}</MenuItem>)}
            </TextField>
          )}

          {actions.canChangePriority && lookups.data && (
            <TextField id="side-priority" select label={t('ticket.priority')} value={ticket.priorityId}
              onChange={(e) => priority.mutate(Number(e.target.value))} disabled={priority.isPending}>
              {lookups.data.priorities.map((p) => <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>)}
            </TextField>
          )}

          {actions.canAssign && assignable.data && (
            <TextField id="side-assignee" select label={t('ticket.assignee')} value={ticket.assigneeUserId ?? 'none'}
              onChange={(e) => assign.mutate(e.target.value === 'none' ? null : Number(e.target.value))} disabled={assign.isPending}>
              <MenuItem value="none">{t('tickets.unassigned')}</MenuItem>
              {assignable.data.map((u) => <MenuItem key={u.userId} value={u.userId}>{u.displayName}</MenuItem>)}
            </TextField>
          )}

          <Box>
            {row(t('ticket.requester'), <>
              <Typography variant="body2">{ticket.requesterName}</Typography>
              {ticket.requesterEmail && <Typography variant="caption" sx={{ color: 'text.secondary' }}>{ticket.requesterEmail}</Typography>}
            </>)}
            {!actions.canAssign && row(t('ticket.assignee'), <Typography variant="body2">{ticket.assigneeName ?? t('tickets.unassigned')}</Typography>)}
            {row(t('ticket.team'), <Typography variant="body2">{ticket.teamName}</Typography>)}
            {row(t('ticket.type'), <Typography variant="body2">{ticket.ticketTypeName}</Typography>)}
            {row(t('ticket.createdAt'), <Typography variant="body2">{formatWhen(ticket.createdAt)}</Typography>)}
            {ticket.firstRespondedAt && row(t('ticket.firstResponse'), <Typography variant="body2">{formatWhen(ticket.firstRespondedAt)}</Typography>)}
            {ticket.resolvedAt && row(t('ticket.resolvedAt'), <Typography variant="body2">{formatWhen(ticket.resolvedAt)}</Typography>)}
          </Box>

          {(actions.canTransfer || actions.canDelete) && (
            <>
              <Divider />
              <Stack direction="row" spacing={1}>
                {actions.canTransfer && <Button variant="outlined" onClick={() => setDialog('transfer')}>{t('ticket.transfer')}</Button>}
                {actions.canDelete && <Button variant="outlined" color="error" onClick={() => setDialog('delete')}>{t('ticket.delete')}</Button>}
              </Stack>
            </>
          )}
          {dialog === 'transfer' && <TransferDialog ticket={ticket} onClose={() => setDialog(null)} onDone={afterDialog} />}
          {dialog === 'delete' && <DeleteDialog ticket={ticket} onClose={() => setDialog(null)} onDone={afterDialog} />}
        </Stack>
      ) : (
        <Stack component="ol" spacing={1.25} sx={{ listStyle: 'none', m: 0, p: 2 }}>
          {ticket.history.map((h, i) => (
            <Box component="li" key={i}>
              <Typography variant="body2">{historyText(h)}</Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>{formatWhen(h.occurredAt)}</Typography>
            </Box>
          ))}
        </Stack>
      )}
    </Paper>
  )
}
