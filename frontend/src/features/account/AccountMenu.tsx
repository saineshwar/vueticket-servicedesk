import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import Divider from '@mui/material/Divider'
import Popover from '@mui/material/Popover'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { orgApi } from '../../api/endpoints'
import { useAuth } from '../../auth/authContext'
import { Person } from '../../components/Person'

/** The signed-in person's name in the top bar; opens who they are and what they can do, and where. */
export function AccountMenu() {
  const { t } = useTranslation()
  const { me, permissions } = useAuth()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const tree = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree, enabled: !!anchor })

  if (!me) return null
  const unitName = (id: number) => tree.data?.find((u) => u.orgUnitId === id)?.name ?? `#${id}`

  // Group by where the access applies: "Network + sub-units → ticket.view, ticket.reply, ...".
  const byUnit = new Map<string, string[]>()
  for (const [key, grants] of Object.entries(permissions)) {
    for (const g of grants) {
      const where = `${g.orgUnitId}|${g.includeDescendants}`
      byUnit.set(where, [...(byUnit.get(where) ?? []), key])
    }
  }

  return (
    <>
      <Button id="account-button" color="inherit" onClick={(e) => setAnchor(e.currentTarget)}
        aria-haspopup="dialog" aria-expanded={anchor ? 'true' : undefined} aria-label={t('account.open', { name: me.displayName })}
        sx={{ display: { xs: 'none', sm: 'inline-flex' }, color: 'text.secondary', fontWeight: 500 }}>
        <Person name={me.displayName} />
      </Button>
      <Popover open={!!anchor} anchorEl={anchor} onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { variant: 'outlined', sx: { width: 380, maxWidth: 'calc(100vw - 32px)', mt: 1 } } }}>
        <Box role="dialog" aria-label={t('account.title')}>
          <Box sx={{ p: 2 }}>
            <Typography sx={{ fontWeight: 650 }}>{me.displayName}</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{me.email}</Typography>
            <Chip size="small" variant="outlined" label={t(`users.types.${me.userType}`, { defaultValue: me.userType })} sx={{ mt: 1 }} />
          </Box>
          <Divider />
          <Box sx={{ p: 2, maxHeight: 360, overflowY: 'auto' }}>
            <Typography variant="overline" component="h2" sx={{ color: 'text.secondary', lineHeight: 2 }}>{t('home.yourAccess')}</Typography>
            {byUnit.size === 0 ? (
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('home.noAccess')}</Typography>
            ) : (
              <Stack spacing={1.5} sx={{ mt: 0.5 }}>
                {[...byUnit.entries()].map(([where, keys]) => {
                  const [unitId, descendants] = where.split('|')
                  return (
                    <Box key={where}>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>
                        {unitName(Number(unitId))}{descendants === 'true' && <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}> {t('account.andBelow')}</Box>}
                      </Typography>
                      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>
                        {keys.sort().map((k) => (
                          <Chip key={k} size="small" variant="outlined" label={k} sx={{ fontFamily: 'Consolas, ui-monospace, monospace', fontWeight: 400 }} />
                        ))}
                      </Stack>
                    </Box>
                  )
                })}
              </Stack>
            )}
          </Box>
        </Box>
      </Popover>
    </>
  )
}
