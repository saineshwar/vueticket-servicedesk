import { api, del, download, get, post, postForm, put } from './http'
import type {
  EngineOverview,
  EngineRun,
  Dashboard,
  Availability,
  AgentStatus,
  BusinessCalendar,
  HolidayList,
  SaveCalendar,
  SaveSlaPolicy,
  SaveUnitSettings,
  SlaOptions,
  SlaPolicy,
  UnitSettings,
  CannedResponse,
  NotificationsResponse,
  SaveCannedResponse,
  AssignableUser,
  CreateTicket,
  CreateUser,
  Lookups,
  SaveCategory,
  SaveServiceDesk,
  ServiceDesk,
  TicketDetail,
  TicketListItem,
  TicketView,
  AccessTokenResponse,
  MeResponse,
  MyPermissionsResponse,
  OrgUnit,
  OrgUnitType,
  PagedResult,
  Permission,
  Role,
  RoleAssignment,
  SetupRequest,
  SetupStatusResponse,
  UserListItem,
} from './types'

export const authApi = {
  login: (userNameOrEmail: string, password: string) =>
    post<AccessTokenResponse>('/auth/login', { userNameOrEmail, password }),
  logout: () => post<void>('/auth/logout'),
}

export const setupApi = {
  status: () => get<SetupStatusResponse>('/setup/status'),
  initialize: (request: SetupRequest) => post<void>('/setup', request),
}

export const meApi = {
  me: () => get<MeResponse>('/me'),
  permissions: () => get<MyPermissionsResponse>('/me/permissions'),
  availability: () => get<Availability>('/me/availability'),
  setAvailability: (status: AgentStatus) => put<Availability>('/me/availability', { status }),
}

export const orgApi = {
  tree: () => get<OrgUnit[]>('/org-units'),
  create: (parentId: number, unitType: OrgUnitType, name: string, code: string) =>
    post<OrgUnit>('/org-units', { parentId, unitType, name, code }),
  update: (id: number, name: string, code: string, isActive: boolean) =>
    put<OrgUnit>(`/org-units/${id}`, { name, code, isActive }),
  move: (id: number, newParentId: number) => post<OrgUnit>(`/org-units/${id}/move`, { newParentId }),
}

export const roleApi = {
  list: () => get<Role[]>('/roles'),
  permissions: () => get<Permission[]>('/permissions'),
  create: (name: string, description: string | null, permissions: string[]) =>
    post<Role>('/roles', { name, description, permissions }),
  update: (id: number, name: string, description: string | null, permissions: string[]) =>
    put<Role>(`/roles/${id}`, { name, description, permissions }),
  remove: (id: number) => del(`/roles/${id}`),
  assignments: (params: { userId?: number; orgUnitId?: number }) => {
    const q = new URLSearchParams()
    if (params.userId) q.set('userId', String(params.userId))
    if (params.orgUnitId) q.set('orgUnitId', String(params.orgUnitId))
    return get<RoleAssignment[]>(`/role-assignments?${q}`)
  },
  assign: (userId: number, roleId: number, orgUnitId: number, includeDescendants: boolean) =>
    post<RoleAssignment>('/role-assignments', { userId, roleId, orgUnitId, includeDescendants }),
  unassign: (id: number) => del(`/role-assignments/${id}`),
}

export const userApi = {
  search: (q: string, page = 1, pageSize = 25) =>
    get<PagedResult<UserListItem>>(`/users?q=${encodeURIComponent(q)}&page=${page}&pageSize=${pageSize}`),
  create: (request: CreateUser) => post<UserListItem>('/users', request),
}

export const deskApi = {
  available: () => get<ServiceDesk[]>('/service-desks'),
  lookups: () => get<Lookups>('/lookups'),
  adminList: () => get<ServiceDesk[]>('/admin/service-desks'),
  create: (request: SaveServiceDesk) => post<ServiceDesk>('/admin/service-desks', request),
  update: (id: number, request: SaveServiceDesk) => put<ServiceDesk>(`/admin/service-desks/${id}`, request),
  createCategory: (deskId: number, request: SaveCategory) => post<ServiceDesk>(`/admin/service-desks/${deskId}/categories`, request),
  updateCategory: (categoryId: number, request: SaveCategory) =>
    put<ServiceDesk>(`/admin/service-desks/categories/${categoryId}`, request),
}

export const ticketApi = {
  list: (params: { view: TicketView; status?: string; q?: string; page?: number; pageSize?: number }) => {
    const query = new URLSearchParams({ view: params.view, page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 25) })
    if (params.status) query.set('status', params.status)
    if (params.q) query.set('q', params.q)
    return get<PagedResult<TicketListItem>>(`/tickets?${query}`)
  },
  get: (id: number) => get<TicketDetail>(`/tickets/${id}`),
  create: (request: CreateTicket, files: File[] = []) => postForm<TicketDetail>('/tickets', request, files),
  comment: (id: number, body: string, isInternal: boolean, newStatus: string | null, files: File[] = []) =>
    postForm<TicketDetail>(`/tickets/${id}/comments`, { body, isInternal, newStatus }, files),
  attach: (id: number, files: File[]) => {
    const form = new FormData()
    files.forEach((file) => form.append('files', file))
    return api<TicketDetail>(`/tickets/${id}/attachments`, { method: 'POST', body: form })
  },
  download: (attachmentId: number, fileName: string) => download(`/attachments/${attachmentId}`, fileName),
  transfer: (id: number, teamId: number) => post<TicketDetail>(`/tickets/${id}/transfer`, { teamId }),
  remove: (id: number, reason: string) => post<TicketDetail>(`/tickets/${id}/delete`, { reason }),
  restore: (id: number) => post<TicketDetail>(`/tickets/${id}/restore`),
  status: (id: number, status: string) => post<TicketDetail>(`/tickets/${id}/status`, { status }),
  priority: (id: number, priorityId: number) => post<TicketDetail>(`/tickets/${id}/priority`, { priorityId }),
  assign: (id: number, assigneeUserId: number | null) => post<TicketDetail>(`/tickets/${id}/assign`, { assigneeUserId }),
  assignable: (id: number) => get<AssignableUser[]>(`/tickets/${id}/assignable-users`),
}

export const cannedApi = {
  list: () => get<CannedResponse[]>('/canned-responses'),
  create: (request: SaveCannedResponse) => post<CannedResponse>('/canned-responses', request),
  update: (id: number, request: SaveCannedResponse) => put<CannedResponse>(`/canned-responses/${id}`, request),
  remove: (id: number) => del(`/canned-responses/${id}`),
}

export const notificationApi = {
  list: () => get<NotificationsResponse>('/notifications?take=20'),
  markRead: (id: number) => post<void>(`/notifications/${id}/read`),
  markAllRead: () => post<void>('/notifications/read-all'),
}

export const slaApi = {
  options: () => get<SlaOptions>('/admin/sla/options'),
  calendars: () => get<BusinessCalendar[]>('/admin/calendars'),
  saveCalendar: (id: number | null, request: SaveCalendar) =>
    id ? put<BusinessCalendar>(`/admin/calendars/${id}`, request) : post<BusinessCalendar>('/admin/calendars', request),
  holidayLists: () => get<HolidayList[]>('/admin/holiday-lists'),
  saveHolidayList: (id: number | null, request: Omit<HolidayList, 'holidayListId'>) =>
    id ? put<HolidayList>(`/admin/holiday-lists/${id}`, request) : post<HolidayList>('/admin/holiday-lists', request),
  policies: () => get<SlaPolicy[]>('/admin/sla-policies'),
  savePolicy: (id: number | null, request: SaveSlaPolicy) =>
    id ? put<SlaPolicy>(`/admin/sla-policies/${id}`, request) : post<SlaPolicy>('/admin/sla-policies', request),
  unitSettings: () => get<UnitSettings[]>('/admin/unit-settings'),
  saveUnitSettings: (id: number, request: SaveUnitSettings) => put<UnitSettings>(`/admin/unit-settings/${id}`, request),
}

export const dashboardApi = {
  get: () => get<Dashboard>('/dashboard'),
}

export const engineApi = {
  overview: () => get<EngineOverview>('/admin/engine'),
  runs: (params: { task?: string; errorsOnly?: boolean; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams({ page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 25) })
    if (params.task) q.set('task', params.task)
    if (params.errorsOnly) q.set('errorsOnly', 'true')
    return get<PagedResult<EngineRun>>(`/admin/engine/runs?${q}`)
  },
}
