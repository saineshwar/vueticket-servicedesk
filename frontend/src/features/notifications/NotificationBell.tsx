import NotificationsNoneOutlined from '@mui/icons-material/NotificationsNoneOutlined'
import Badge from '@mui/material/Badge'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { notificationApi } from '../../api/endpoints'
import type { AppNotification } from '../../api/types'
import { formatWhen } from '../tickets/ticketFormat'

/** Unread count refreshes every 30 seconds. Real-time push (SignalR) replaces polling in a later phase. */
export function NotificationBell() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)

  const list = useQuery({ queryKey: ['notifications'], queryFn: notificationApi.list, refetchInterval: 30_000 })
  const refresh = () => qc.invalidateQueries({ queryKey: ['notifications'] })
  const markAll = useMutation({ mutationFn: notificationApi.markAllRead, onSuccess: refresh })

  const open = async (n: AppNotification) => {
    setAnchor(null)
    if (!n.readAt) {
      await notificationApi.markRead(n.notificationId)
      void refresh()
    }
    if (n.ticketId) navigate(`/tickets/${n.ticketId}`)
  }

  const unread = list.data?.unreadCount ?? 0

  return (
    <>
      <IconButton aria-label={t('notifications.label', { count: unread })} onClick={(e) => setAnchor(e.currentTarget)}>
        <Badge badgeContent={unread} color="error" max={99}>
          <NotificationsNoneOutlined />
        </Badge>
      </IconButton>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}
        slotProps={{ paper: { sx: { width: 360, maxWidth: 'calc(100vw - 32px)' } } }}>
        <Stack direction="row" sx={{ px: 2, py: 1, alignItems: 'center' }}>
          <Typography sx={{ fontWeight: 650, flexGrow: 1 }}>{t('notifications.title')}</Typography>
          {unread > 0 && <Button size="small" onClick={() => markAll.mutate()}>{t('notifications.markAll')}</Button>}
        </Stack>
        <Divider />
        {list.data?.items.length === 0 && (
          <Box sx={{ px: 2, py: 2 }}><Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('notifications.empty')}</Typography></Box>
        )}
        {list.data?.items.map((n) => (
          <MenuItem key={n.notificationId} onClick={() => void open(n)} sx={{ whiteSpace: 'normal', alignItems: 'flex-start', gap: 1 }}>
            <Box sx={{ width: 8, height: 8, borderRadius: '50%', mt: 1, flexShrink: 0, bgcolor: n.readAt ? 'transparent' : 'primary.main' }} />
            <ListItemText
              primary={n.title}
              secondary={formatWhen(n.createdAt)}
              slotProps={{ primary: { variant: 'body2', sx: { fontWeight: n.readAt ? 400 : 600 } } }}
            />
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
