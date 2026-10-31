import FiberManualRecord from '@mui/icons-material/FiberManualRecord'
import Button from '@mui/material/Button'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { meApi } from '../../api/endpoints'
import type { AgentStatus } from '../../api/types'

const statuses: AgentStatus[] = ['Available', 'Busy', 'Away', 'Offline']
const dotColor: Record<AgentStatus, string> = { Available: 'success.main', Busy: 'error.main', Away: 'warning.main', Offline: 'text.disabled' }

/** Agents set their status here; only Available agents receive automatically assigned tickets. */
export function AvailabilityMenu() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const current = useQuery({ queryKey: ['availability'], queryFn: meApi.availability })
  const change = useMutation({
    mutationFn: meApi.setAvailability,
    onSuccess: (data) => qc.setQueryData(['availability'], data),
  })

  const status = current.data?.status ?? 'Offline'
  return (
    <>
      <Button
        id="availability-button"
        color="inherit"
        onClick={(e) => setAnchor(e.currentTarget)}
        startIcon={<FiberManualRecord sx={{ color: dotColor[status], fontSize: '12px !important' }} />}
        aria-haspopup="menu"
        aria-expanded={anchor ? 'true' : undefined}
        aria-label={t('availability.label', { status: t(`availability.${status}`) })}
        disabled={current.isLoading}
        sx={{ textTransform: 'none', color: 'text.secondary' }}
      >
        {t(`availability.${status}`)}
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        {statuses.map((s) => (
          <MenuItem key={s} selected={s === status} disabled={change.isPending}
            onClick={() => { setAnchor(null); if (s !== status) change.mutate(s) }}>
            <ListItemIcon><FiberManualRecord sx={{ color: dotColor[s], fontSize: 12 }} /></ListItemIcon>
            <ListItemText primary={t(`availability.${s}`)} secondary={s === 'Available' ? t('availability.availableHelp') : undefined} />
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
