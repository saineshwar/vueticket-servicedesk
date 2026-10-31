// Mirrors TicketCore.ViewModels. To be replaced by an OpenAPI-generated client (Orval) once the API settles.

export interface AccessTokenResponse {
  accessToken: string
  expiresAt: string
}

export interface SetupStatusResponse {
  isInitialized: boolean
}

export interface SetupRequest {
  organizationName: string
  organizationCode: string
  adminUserName: string
  adminEmail: string
  adminFirstName: string
  adminLastName: string
  adminPassword: string
}

export interface MeResponse {
  userId: number
  userName: string
  email: string
  displayName: string
  userType: 'Customer' | 'Employee' | 'Staff'
  homeOrgUnitId: number | null
}

export interface GrantScope {
  orgUnitId: number
  includeDescendants: boolean
}

export interface MyPermissionsResponse {
  permissions: Record<string, GrantScope[]>
}

export type OrgUnitType = 'Organization' | 'BusinessUnit' | 'Department' | 'Team'

export interface OrgUnit {
  orgUnitId: number
  parentId: number | null
  unitType: OrgUnitType
  name: string
  code: string
  isActive: boolean
  depth: number
  memberCount: number
}

export interface Permission {
  key: string
  group: string
  description: string
}

export interface Role {
  roleId: number
  name: string
  description: string | null
  isSystem: boolean
  permissions: string[]
  assignmentCount: number
}

export interface RoleAssignment {
  roleAssignmentId: number
  userId: number
  userDisplayName: string
  roleId: number
  roleName: string
  orgUnitId: number
  orgUnitName: string
  includeDescendants: boolean
  createdAt: string
}

export interface UserListItem {
  userId: number
  userName: string
  email: string
  displayName: string
  userType: string
  homeOrgUnitId: number | null
  homeOrgUnitName: string | null
  isActive: boolean
}

export interface LookupItem {
  id: number
  name: string
}

export interface Lookups {
  priorities: LookupItem[]
  ticketTypes: LookupItem[]
}

export type DeskAudience = 'Employees' | 'Customers' | 'Both'

export interface Category {
  categoryId: number
  serviceDeskId: number
  name: string
  description: string | null
  teamId: number | null
  teamName: string | null
  ticketTypeId: number
  ticketTypeName: string
  defaultPriorityId: number
  sortOrder: number
  isActive: boolean
}

export interface ServiceDesk {
  serviceDeskId: number
  name: string
  code: string
  description: string | null
  audience: DeskAudience
  defaultTeamId: number
  defaultTeamName: string
  isActive: boolean
  categories: Category[]
  /** Resolved tickets close after this many hours without a requester reply; 0 never. */
  autoCloseHours: number
}

export interface SaveServiceDesk {
  name: string
  code: string
  description: string | null
  audience: DeskAudience
  defaultTeamId: number
  isActive: boolean
  autoCloseHours: number
}

export interface SaveCategory {
  name: string
  description: string | null
  teamId: number | null
  ticketTypeId: number
  defaultPriorityId: number
  sortOrder: number
  isActive: boolean
}

export type TicketStatus =
  | 'New'
  | 'Open'
  | 'InProgress'
  | 'AwaitingRequester'
  | 'Resolved'
  | 'Closed'
  | 'PendingApproval'
  | 'Rejected'

export type TicketView = 'Requested' | 'Mine' | 'Team' | 'Unassigned' | 'Escalated' | 'Deleted'

export interface TicketListItem {
  ticketId: number
  trackingId: string
  subject: string
  status: TicketStatus
  priorityId: number
  priorityName: string
  serviceDeskName: string
  categoryName: string
  teamName: string
  requesterName: string
  assigneeName: string | null
  createdAt: string
  updatedAt: string
  /** Earliest SLA target not yet met; null when none or paused. Not sent for "Requested". */
  slaDueAt: string | null
  slaPaused: boolean
  escalationLevel: number | null
}

export interface TicketComment {
  ticketCommentId: number
  authorUserId: number
  authorName: string
  authorIsSupport: boolean
  isInternal: boolean
  body: string
  createdAt: string
}

export interface TicketHistory {
  activity: string
  fromValue: string | null
  toValue: string | null
  userName: string | null
  occurredAt: string
}

export interface TicketActions {
  canReply: boolean
  canAddNote: boolean
  canChangeStatus: boolean
  canChangePriority: boolean
  canAssign: boolean
  canTransfer: boolean
  canDelete: boolean
  canRestore: boolean
  canAttach: boolean
  allowedStatuses: TicketStatus[]
}

export interface Attachment {
  attachmentId: number
  ticketCommentId: number | null
  fileName: string
  contentType: string
  sizeBytes: number
  isInternal: boolean
  uploadedByName: string
  uploadedAt: string
}

export interface CannedResponse {
  cannedResponseId: number
  title: string
  body: string
  teamId: number | null
  teamName: string | null
  isPersonal: boolean
  canEdit: boolean
}

export interface SaveCannedResponse {
  title: string
  body: string
  teamId: number | null
}

export interface AppNotification {
  notificationId: number
  ticketId: number | null
  type: string
  title: string
  createdAt: string
  readAt: string | null
}

export interface NotificationsResponse {
  items: AppNotification[]
  unreadCount: number
}

export interface TicketDetail {
  ticketId: number
  trackingId: string
  subject: string
  description: string
  status: TicketStatus
  priorityId: number
  priorityName: string
  serviceDeskId: number
  serviceDeskName: string
  categoryId: number
  categoryName: string
  ticketTypeName: string
  teamId: number
  teamName: string
  requesterUserId: number
  requesterName: string
  requesterEmail: string
  assigneeUserId: number | null
  assigneeName: string | null
  channel: string
  createdAt: string
  updatedAt: string
  firstRespondedAt: string | null
  resolvedAt: string | null
  viewerIsSupport: boolean
  isDeleted: boolean
  deletedAt: string | null
  deleteReason: string | null
  actions: TicketActions
  comments: TicketComment[]
  attachments: Attachment[]
  history: TicketHistory[]
  /** Support staff only. */
  sla: TicketSla | null
  escalation: TicketEscalation | null
}

export interface CreateTicket {
  serviceDeskId: number
  categoryId: number
  subject: string
  description: string
  priorityId: number | null
  requesterUserId: number | null
}

export interface AssignableUser {
  userId: number
  displayName: string
}

export interface CreateUser {
  userName: string
  email: string
  firstName: string | null
  lastName: string | null
  userType: 'Customer' | 'Employee' | 'Staff'
  homeOrgUnitId: number | null
  managerUserId: number | null
  password: string
}

export interface PagedResult<T> {
  items: T[]
  total: number
}

// ---- SLA and escalation ----

export type SlaClockName = 'first_response' | 'next_response' | 'resolution'

export interface SlaClock {
  clock: SlaClockName
  dueAt: string | null
  metAt: string | null
  breachedAt: string | null
}

export interface TicketSla {
  policyName: string | null
  clocks: SlaClock[]
  pausedAt: string | null
}

export interface TicketEscalation {
  level: number
  roleName: string | null
  reasons: SlaClockName[]
  ownerUserId: number | null
  ownerName: string | null
  startedAt: string
  windowEndsAt: string | null
}

export type AgentStatus = 'Available' | 'Busy' | 'Away' | 'Offline'

export interface Availability {
  status: AgentStatus
  changedAt: string | null
}

// ---- Calendars, SLA policies, unit settings ----

export interface CalendarHours {
  dayOfWeek: number
  startMinute: number
  endMinute: number
}

export interface BusinessCalendar {
  calendarId: number
  name: string
  timeZoneId: string
  is24x7: boolean
  isDefault: boolean
  isActive: boolean
  unitsUsing: number
  hours: CalendarHours[]
  holidayListIds: number[]
}

export type SaveCalendar = Omit<BusinessCalendar, 'calendarId' | 'unitsUsing'>

export interface Holiday {
  date: string
  name: string
}

export interface HolidayList {
  holidayListId: number
  name: string
  holidays: Holiday[]
}

export interface EscalationLevel {
  level: number
  roleId: number
  roleName: string | null
  windowMinutes: number | null
}

export interface SlaPolicy {
  slaPolicyId: number
  name: string
  serviceDeskId: number | null
  serviceDeskName: string | null
  ticketTypeId: number | null
  ticketTypeName: string | null
  priorityId: number
  priorityName: string
  firstResponseMinutes: number
  nextResponseMinutes: number
  resolutionMinutes: number
  reminderMinutes: number
  escalateOnFirstResponse: boolean
  escalateOnNextResponse: boolean
  escalateOnResolution: boolean
  isActive: boolean
  levels: EscalationLevel[]
}

export type SaveSlaPolicy = Omit<SlaPolicy, 'slaPolicyId' | 'serviceDeskName' | 'ticketTypeName' | 'priorityName'>

export interface SlaOptions {
  desks: LookupItem[]
  ticketTypes: LookupItem[]
  priorities: LookupItem[]
  roles: LookupItem[]
  calendars: LookupItem[]
}

export type AssignmentMode = 'Manual' | 'LoadBalanced' | 'RoundRobin'

export interface UnitSettings {
  orgUnitId: number
  parentId: number | null
  unitType: OrgUnitType
  name: string
  code: string
  isActive: boolean
  calendarId: number | null
  effectiveCalendarName: string | null
  calendarInherited: boolean
  assignmentMode: AssignmentMode | null
  maxOpenTickets: number | null
  unassignedAlertMinutes: number | null
  canEditCalendar: boolean
  canEditAssignment: boolean
}

export interface SaveUnitSettings {
  calendarId: number | null
  assignmentMode: AssignmentMode | null
  maxOpenTickets: number | null
  unassignedAlertMinutes: number | null
}

// ---- Home dashboard ----

export interface MyRequestsSummary {
  open: number
  awaitingYou: number
  resolvedLast30Days: number
  recent: TicketListItem[]
}

export interface MyWorkSummary {
  assignedOpen: number
  overdue: number
  dueWithinHour: number
  /** Null when the caller cannot work the unassigned queue. */
  unassigned: number | null
  escalated: number | null
  nextUp: TicketListItem[]
}

export interface AgentLoad {
  userId: number
  name: string
  status: AgentStatus
  open: number
  overdue: number
}

export interface DailyFlow {
  date: string
  created: number
  resolved: number
}

export interface TeamSummary {
  open: number
  unassigned: number
  overdue: number
  escalated: number
  openByPriority: { priorityId: number; priorityName: string; count: number }[]
  agents: AgentLoad[]
  last14Days: DailyFlow[]
}

export interface Dashboard {
  requests: MyRequestsSummary
  work: MyWorkSummary | null
  team: TeamSummary | null
}

// ---- Engine monitoring ----

export type EngineHealth = 'Running' | 'Late' | 'Failing' | 'Stopped' | 'Disabled'

export interface EngineTaskState {
  instanceId: string
  task: string
  enabled: boolean
  intervalSeconds: number
  instanceStartedAt: string
  lastRunAt: string | null
  lastDurationMs: number | null
  lastProcessed: number
  lastSuccessAt: string | null
  lastErrorAt: string | null
  lastError: string | null
  runs: number
  failures: number
  health: EngineHealth
  nextRunAt: string | null
}

export interface EngineStream {
  lastEventId: number
  latestEventId: number
  backlog: number
  leaseOwner: string | null
  leaseUntil: string | null
  updatedAt: string | null
}

export interface EngineOverview {
  now: string
  tasks: EngineTaskState[]
  stream: EngineStream | null
}

export interface EngineRun {
  engineRunLogId: number
  instanceId: string
  task: string
  startedAt: string
  durationMs: number
  processed: number
  succeeded: boolean
  error: string | null
}
