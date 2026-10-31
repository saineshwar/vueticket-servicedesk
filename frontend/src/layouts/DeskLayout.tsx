import AccountTreeOutlined from '@mui/icons-material/AccountTreeOutlined'
import AddCircleOutline from '@mui/icons-material/AddCircleOutlined'
import BookmarkBorderOutlined from '@mui/icons-material/BookmarkBorderOutlined'
import PeopleOutline from '@mui/icons-material/PeopleOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined'
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined'
import AdminPanelSettingsOutlined from '@mui/icons-material/AdminPanelSettingsOutlined'
import EventOutlined from '@mui/icons-material/EventOutlined'
import HomeOutlined from '@mui/icons-material/HomeOutlined'
import TimerOutlined from '@mui/icons-material/TimerOutlined'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
import MenuIcon from '@mui/icons-material/Menu'
import MonitorHeartOutlined from '@mui/icons-material/MonitorHeartOutlined'
import AppBar from '@mui/material/AppBar'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Drawer from '@mui/material/Drawer'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItemButton from '@mui/material/ListItemButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import ListSubheader from '@mui/material/ListSubheader'
import Toolbar from '@mui/material/Toolbar'
import Typography from '@mui/material/Typography'
import useMediaQuery from '@mui/material/useMediaQuery'
import { Suspense, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/authContext'
import { AccountMenu } from '../features/account/AccountMenu'
import { AvailabilityMenu } from '../features/availability/AvailabilityMenu'
import { NotificationBell } from '../features/notifications/NotificationBell'

const drawerWidth = 248

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  /** Shown when the user holds any of these permissions; empty means always. */
  anyOf: string[]
}

export function DeskLayout() {
  const { t } = useTranslation()
  const { can, logout } = useAuth()
  const wide = useMediaQuery('(min-width: 900px)')
  const [open, setOpen] = useState(false)

  const main: NavItem[] = [
    { to: '/', label: t('nav.home'), icon: <HomeOutlined />, anyOf: [] },
    { to: '/requests/new', label: t('nav.newRequest'), icon: <AddCircleOutline />, anyOf: [] },
    { to: '/requests', label: t('nav.myRequests'), icon: <ReceiptLongOutlined />, anyOf: [] },
  ]
  const support: NavItem[] = [
    { to: '/tickets', label: t('nav.tickets'), icon: <SupportAgentOutlined />, anyOf: ['ticket.view'] },
    { to: '/saved-replies', label: t('nav.savedReplies'), icon: <BookmarkBorderOutlined />, anyOf: ['ticket.reply'] },
  ].filter((item) => item.anyOf.some(can))
  const admin: NavItem[] = [
    { to: '/admin/organization', label: t('nav.organization'), icon: <AccountTreeOutlined />, anyOf: ['org.manage', 'role.manage'] },
    { to: '/admin/roles', label: t('nav.roles'), icon: <AdminPanelSettingsOutlined />, anyOf: ['role.manage'] },
    { to: '/admin/users', label: t('nav.users'), icon: <PeopleOutline />, anyOf: ['user.manage'] },
    { to: '/admin/service-desks', label: t('nav.serviceDesks'), icon: <StorefrontOutlined />, anyOf: ['config.desks'] },
    { to: '/admin/sla-policies', label: t('nav.slaPolicies'), icon: <TimerOutlined />, anyOf: ['config.sla'] },
    { to: '/admin/calendars', label: t('nav.calendars'), icon: <EventOutlined />, anyOf: ['config.calendars'] },
    { to: '/admin/hours-and-assignment', label: t('nav.hoursAndAssignment'), icon: <TuneOutlined />, anyOf: ['config.calendars', 'config.routing'] },
    { to: '/admin/engine', label: t('nav.engine'), icon: <MonitorHeartOutlined />, anyOf: ['config.jobs'] },
  ].filter((item) => item.anyOf.some(can))

  const renderItems = (items: NavItem[]) =>
    items.map((item) => (
      <ListItemButton
        key={item.to}
        component={NavLink}
        to={item.to}
        end={item.to === '/' || item.to === '/requests'}
        onClick={() => setOpen(false)}
        sx={{
          borderRadius: 1.5, mx: 1, my: 0.25, color: 'text.secondary',
          '& .MuiListItemIcon-root': { color: 'text.secondary' },
          '&:hover': { color: 'text.primary' },
          '&.active': { bgcolor: 'action.selected', color: 'primary.main', '& .MuiListItemIcon-root': { color: 'primary.main' }, '& .MuiListItemText-primary': { fontWeight: 600 } },
        }}
      >
        <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
        <ListItemText primary={item.label} />
      </ListItemButton>
    ))

  const nav = (
    <Box component="nav" aria-label="Main" sx={{ py: 1 }}>
      <List dense>{renderItems(main)}</List>
      {support.length > 0 && (
        <List dense subheader={<ListSubheader sx={{ bgcolor: 'transparent', fontSize: '0.7rem', fontWeight: 700, letterSpacing: '0.06em', textTransform: 'uppercase', lineHeight: '36px', mt: 1 }}>{t('nav.support')}</ListSubheader>}>
          {renderItems(support)}
        </List>
      )}
      {admin.length > 0 && (
        <List dense subheader={<ListSubheader sx={{ bgcolor: 'transparent', fontSize: '0.7rem', fontWeight: 700, letterSpacing: '0.06em', textTransform: 'uppercase', lineHeight: '36px', mt: 1 }}>{t('nav.administration')}</ListSubheader>}>
          {renderItems(admin)}
        </List>
      )}
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="fixed" color="inherit" sx={{ zIndex: (theme) => theme.zIndex.drawer + 1, borderBottom: 1, borderColor: 'divider' }}>
        <Toolbar sx={{ gap: 1 }}>
          {!wide && (
            <IconButton edge="start" aria-label="Open menu" onClick={() => setOpen(true)}>
              <MenuIcon />
            </IconButton>
          )}
          <Typography component="span" sx={{ fontWeight: 700, color: 'primary.main', flexGrow: 1 }}>
            {t('app.name')}
          </Typography>
          {can('agent.checkin') && <AvailabilityMenu />}
          <NotificationBell />
          <AccountMenu />
          <Button onClick={() => void logout()}>{t('app.signOut')}</Button>
        </Toolbar>
      </AppBar>

      <Drawer
        variant={wide ? 'permanent' : 'temporary'}
        open={wide || open}
        onClose={() => setOpen(false)}
        sx={{ width: drawerWidth, flexShrink: 0, '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' } }}
      >
        <Toolbar />
        {nav}
      </Drawer>

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 2, md: 3 } }}>
        <Toolbar />
        <Suspense fallback={<CircularProgress aria-label={t('app.loading')} />}>
          <Outlet />
        </Suspense>
      </Box>
    </Box>
  )
}
