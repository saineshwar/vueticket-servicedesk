import BookmarkBorderOutlined from '@mui/icons-material/BookmarkBorderOutlined'
import Button from '@mui/material/Button'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { cannedApi } from '../../api/endpoints'
import type { TicketDetail } from '../../api/types'
import { useAuth } from '../../auth/authContext'
import { fillPlaceholders } from './savedReplyText'

/** Inserts a saved reply into the reply box, with placeholders filled in for this ticket. */
export function SavedRepliesMenu({ ticket, onInsert }: { ticket: TicketDetail; onInsert: (text: string) => void }) {
  const { t } = useTranslation()
  const { me } = useAuth()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const replies = useQuery({ queryKey: ['canned'], queryFn: cannedApi.list })

  if (!replies.data || replies.data.length === 0) return null

  return (
    <>
      <Button size="small" startIcon={<BookmarkBorderOutlined />} onClick={(e) => setAnchor(e.currentTarget)}>
        {t('savedReplies.insert')}
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        {replies.data.map((r) => (
          <MenuItem key={r.cannedResponseId} onClick={() => { onInsert(fillPlaceholders(r.body, ticket, me?.displayName ?? '')); setAnchor(null) }}>
            <ListItemText primary={r.title} secondary={r.isPersonal ? t('savedReplies.personal') : r.teamName} />
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
