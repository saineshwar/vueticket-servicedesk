import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import { lazy, type ReactNode } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { useAuth } from '../auth/authContext'
import { LoginPage } from '../features/auth/LoginPage'
import { SetupPage } from '../features/auth/SetupPage'
import { HomePage } from '../features/home/HomePage'
import { DeskLayout } from '../layouts/DeskLayout'

// Pages load on first visit, so signing in downloads only the shell and the home page.
const ServiceDesksPage = lazy(() => import('../features/admin/desks/ServiceDesksPage').then((m) => ({ default: m.ServiceDesksPage })))
const OrgPage = lazy(() => import('../features/admin/org/OrgPage').then((m) => ({ default: m.OrgPage })))
const UsersPage = lazy(() => import('../features/admin/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const NewTicketPage = lazy(() => import('../features/tickets/NewTicketPage').then((m) => ({ default: m.NewTicketPage })))
const TicketDetailPage = lazy(() => import('../features/tickets/TicketDetailPage').then((m) => ({ default: m.TicketDetailPage })))
const TicketListPage = lazy(() => import('../features/tickets/TicketListPage').then((m) => ({ default: m.TicketListPage })))
const SavedRepliesPage = lazy(() => import('../features/tickets/SavedRepliesPage').then((m) => ({ default: m.SavedRepliesPage })))
const RolesPage = lazy(() => import('../features/admin/roles/RolesPage').then((m) => ({ default: m.RolesPage })))
const CalendarsPage = lazy(() => import('../features/admin/sla/CalendarsPage').then((m) => ({ default: m.CalendarsPage })))
const SlaPoliciesPage = lazy(() => import('../features/admin/sla/SlaPoliciesPage').then((m) => ({ default: m.SlaPoliciesPage })))
const EnginePage = lazy(() => import('../features/admin/engine/EnginePage').then((m) => ({ default: m.EnginePage })))
const UnitSettingsPage = lazy(() => import('../features/admin/sla/UnitSettingsPage').then((m) => ({ default: m.UnitSettingsPage })))

function RequirePermission({ anyOf, children }: { anyOf: string[]; children: ReactNode }) {
  const { can } = useAuth()
  return anyOf.some(can) ? children : <Navigate to="/" replace />
}

const signedInRouter = createBrowserRouter([
  {
    element: <DeskLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'requests', element: <TicketListPage mode="requests" /> },
      { path: 'requests/new', element: <NewTicketPage /> },
      { path: 'tickets', element: <RequirePermission anyOf={['ticket.view']}><TicketListPage mode="queues" /></RequirePermission> },
      // Access to a single ticket is decided by the API (requester, assignee or team scope).
      { path: 'tickets/:id', element: <TicketDetailPage /> },
      { path: 'saved-replies', element: <RequirePermission anyOf={['ticket.reply']}><SavedRepliesPage /></RequirePermission> },
      {
        path: 'admin/users',
        element: <RequirePermission anyOf={['user.manage']}><UsersPage /></RequirePermission>,
      },
      {
        path: 'admin/service-desks',
        element: <RequirePermission anyOf={['config.desks']}><ServiceDesksPage /></RequirePermission>,
      },
      {
        path: 'admin/organization',
        element: <RequirePermission anyOf={['org.manage', 'role.manage']}><OrgPage /></RequirePermission>,
      },
      {
        path: 'admin/roles',
        element: <RequirePermission anyOf={['role.manage']}><RolesPage /></RequirePermission>,
      },
      {
        path: 'admin/sla-policies',
        element: <RequirePermission anyOf={['config.sla']}><SlaPoliciesPage /></RequirePermission>,
      },
      {
        path: 'admin/calendars',
        element: <RequirePermission anyOf={['config.calendars']}><CalendarsPage /></RequirePermission>,
      },
      {
        path: 'admin/engine',
        element: <RequirePermission anyOf={['config.jobs']}><EnginePage /></RequirePermission>,
      },
      {
        path: 'admin/hours-and-assignment',
        element: <RequirePermission anyOf={['config.calendars', 'config.routing']}><UnitSettingsPage /></RequirePermission>,
      },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])

export function App() {
  const { status } = useAuth()

  if (status === 'loading') {
    return (
      <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  }
  if (status === 'needs-setup') return <SetupPage />
  if (status === 'anonymous') return <LoginPage />
  return <RouterProvider router={signedInRouter} />
}
