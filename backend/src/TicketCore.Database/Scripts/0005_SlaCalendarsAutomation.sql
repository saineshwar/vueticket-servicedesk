-- 0005: Business calendars, SLA policies and escalation paths, per-ticket SLA state, escalations,
--       team assignment settings, agent availability, auto-close setting.

-- ---------- Calendars ----------
CREATE TABLE dbo.Calendar
(
    CalendarId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Calendar PRIMARY KEY,
    Name        NVARCHAR(100) NOT NULL CONSTRAINT UQ_Calendar_Name UNIQUE,
    TimeZoneId  VARCHAR(64) NOT NULL,      -- IANA id, e.g. Asia/Kolkata
    Is24x7      BIT NOT NULL,
    IsDefault   BIT NOT NULL CONSTRAINT DF_Calendar_IsDefault DEFAULT (0),
    IsActive    BIT NOT NULL CONSTRAINT DF_Calendar_IsActive DEFAULT (1)
);
CREATE UNIQUE INDEX UX_Calendar_Default ON dbo.Calendar(IsDefault) WHERE IsDefault = 1;

-- Working windows per weekday (0 = Sunday ... 6 = Saturday), minutes from local midnight. Several windows per day allowed.
CREATE TABLE dbo.CalendarHours
(
    CalendarHoursId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CalendarHours PRIMARY KEY,
    CalendarId      INT NOT NULL CONSTRAINT FK_CalendarHours_Calendar REFERENCES dbo.Calendar(CalendarId) ON DELETE CASCADE,
    DayOfWeek       TINYINT NOT NULL CONSTRAINT CK_CalendarHours_Day CHECK (DayOfWeek BETWEEN 0 AND 6),
    StartMinute     SMALLINT NOT NULL,
    EndMinute       SMALLINT NOT NULL,
    CONSTRAINT CK_CalendarHours_Window CHECK (StartMinute >= 0 AND EndMinute <= 1440 AND StartMinute < EndMinute)
);

CREATE TABLE dbo.HolidayList
(
    HolidayListId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HolidayList PRIMARY KEY,
    Name          NVARCHAR(100) NOT NULL CONSTRAINT UQ_HolidayList_Name UNIQUE
);

CREATE TABLE dbo.Holiday
(
    HolidayId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Holiday PRIMARY KEY,
    HolidayListId INT NOT NULL CONSTRAINT FK_Holiday_List REFERENCES dbo.HolidayList(HolidayListId) ON DELETE CASCADE,
    HolidayDate   DATE NOT NULL,
    Name          NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Holiday UNIQUE (HolidayListId, HolidayDate)
);

CREATE TABLE dbo.CalendarHolidayList
(
    CalendarId    INT NOT NULL CONSTRAINT FK_CalendarHolidayList_Calendar REFERENCES dbo.Calendar(CalendarId) ON DELETE CASCADE,
    HolidayListId INT NOT NULL CONSTRAINT FK_CalendarHolidayList_List REFERENCES dbo.HolidayList(HolidayListId) ON DELETE CASCADE,
    CONSTRAINT PK_CalendarHolidayList PRIMARY KEY (CalendarId, HolidayListId)
);

-- A unit without a calendar inherits its parent's; the root falls back to the default calendar.
ALTER TABLE dbo.OrgUnit ADD CalendarId INT NULL CONSTRAINT FK_OrgUnit_Calendar REFERENCES dbo.Calendar(CalendarId);
GO

-- ---------- Team assignment settings ----------
CREATE TABLE dbo.TeamSettings
(
    TeamId                 INT NOT NULL CONSTRAINT PK_TeamSettings PRIMARY KEY CONSTRAINT FK_TeamSettings_Team REFERENCES dbo.OrgUnit(OrgUnitId),
    AssignmentMode         TINYINT NOT NULL CONSTRAINT DF_TeamSettings_Mode DEFAULT (1), -- 0 Manual, 1 Load-balanced, 2 Round-robin
    MaxOpenTickets         INT NOT NULL CONSTRAINT DF_TeamSettings_Max DEFAULT (20),
    UnassignedAlertMinutes INT NOT NULL CONSTRAINT DF_TeamSettings_Alert DEFAULT (15)
);

-- ---------- Agent availability ----------
CREATE TABLE dbo.AgentAvailability
(
    UserId         BIGINT NOT NULL CONSTRAINT PK_AgentAvailability PRIMARY KEY CONSTRAINT FK_AgentAvailability_User REFERENCES dbo.[User](UserId),
    Status         TINYINT NOT NULL, -- 1 Available, 2 Busy, 3 Away, 4 Offline
    ChangedAt      DATETIME2(3) NOT NULL,
    LastAssignedAt DATETIME2(3) NULL
);

CREATE TABLE dbo.AgentAvailabilityLog
(
    AgentAvailabilityLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AgentAvailabilityLog PRIMARY KEY,
    UserId                 BIGINT NOT NULL CONSTRAINT FK_AgentAvailabilityLog_User REFERENCES dbo.[User](UserId),
    Status                 TINYINT NOT NULL,
    ChangedAt              DATETIME2(3) NOT NULL,
    Automatic              BIT NOT NULL
);
CREATE INDEX IX_AgentAvailabilityLog_User ON dbo.AgentAvailabilityLog(UserId, ChangedAt);

-- ---------- SLA policies and escalation paths ----------
-- Matching: most specific wins (desk + type, then desk, then type, then global) for the ticket's priority.
CREATE TABLE dbo.SlaPolicy
(
    SlaPolicyId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SlaPolicy PRIMARY KEY,
    Name                    NVARCHAR(100) NOT NULL,
    ServiceDeskId           INT NULL CONSTRAINT FK_SlaPolicy_Desk REFERENCES dbo.ServiceDesk(ServiceDeskId),
    TicketTypeId            TINYINT NULL CONSTRAINT FK_SlaPolicy_Type REFERENCES dbo.TicketType(TicketTypeId),
    PriorityId              TINYINT NOT NULL CONSTRAINT FK_SlaPolicy_Priority REFERENCES dbo.Priority(PriorityId),
    FirstResponseMinutes    INT NOT NULL,
    NextResponseMinutes     INT NOT NULL,
    ResolutionMinutes       INT NOT NULL,
    ReminderMinutes         INT NOT NULL,
    EscalateOnFirstResponse BIT NOT NULL,
    EscalateOnNextResponse  BIT NOT NULL,
    EscalateOnResolution    BIT NOT NULL,
    IsActive                BIT NOT NULL CONSTRAINT DF_SlaPolicy_IsActive DEFAULT (1),
    CONSTRAINT CK_SlaPolicy_Minutes CHECK (FirstResponseMinutes > 0 AND NextResponseMinutes > 0 AND ResolutionMinutes > 0 AND ReminderMinutes >= 0)
);
CREATE UNIQUE INDEX UX_SlaPolicy_Scope ON dbo.SlaPolicy(ServiceDeskId, TicketTypeId, PriorityId) WHERE IsActive = 1;

-- Each level targets a role relative to the ticket's team (found by walking up the tree).
-- WindowMinutes = how long that level has before the next one; NULL on the final level.
CREATE TABLE dbo.EscalationLevel
(
    SlaPolicyId   INT NOT NULL CONSTRAINT FK_EscalationLevel_Policy REFERENCES dbo.SlaPolicy(SlaPolicyId) ON DELETE CASCADE,
    Level         TINYINT NOT NULL CONSTRAINT CK_EscalationLevel_Level CHECK (Level BETWEEN 1 AND 4),
    RoleId        INT NOT NULL CONSTRAINT FK_EscalationLevel_Role REFERENCES dbo.Role(RoleId),
    WindowMinutes INT NULL,
    CONSTRAINT PK_EscalationLevel PRIMARY KEY (SlaPolicyId, Level)
);

-- ---------- Per-ticket SLA state ----------
CREATE TABLE dbo.TicketSla
(
    TicketId                BIGINT NOT NULL CONSTRAINT PK_TicketSla PRIMARY KEY CONSTRAINT FK_TicketSla_Ticket REFERENCES dbo.Ticket(TicketId),
    SlaPolicyId             INT NULL CONSTRAINT FK_TicketSla_Policy REFERENCES dbo.SlaPolicy(SlaPolicyId),
    CalendarId              INT NULL CONSTRAINT FK_TicketSla_Calendar REFERENCES dbo.Calendar(CalendarId),
    FirstResponseDueAt      DATETIME2(3) NULL,
    FirstResponseRemindAt   DATETIME2(3) NULL, -- reminder moment, in business time before the due time
    FirstResponseMetAt      DATETIME2(3) NULL,
    FirstResponseRemindedAt DATETIME2(3) NULL,
    FirstResponseBreachedAt DATETIME2(3) NULL,
    NextResponseDueAt       DATETIME2(3) NULL,
    NextResponseRemindAt    DATETIME2(3) NULL,
    NextResponseRemindedAt  DATETIME2(3) NULL,
    NextResponseBreachedAt  DATETIME2(3) NULL,
    ResolutionDueAt         DATETIME2(3) NULL,
    ResolutionRemindAt      DATETIME2(3) NULL,
    ResolutionMetAt         DATETIME2(3) NULL,
    ResolutionRemindedAt    DATETIME2(3) NULL,
    ResolutionBreachedAt    DATETIME2(3) NULL,
    PausedAt                DATETIME2(3) NULL,
    PausedMinutes           INT NOT NULL CONSTRAINT DF_TicketSla_PausedMinutes DEFAULT (0), -- business minutes spent awaiting the requester
    UnassignedAlert1At      DATETIME2(3) NULL,
    UnassignedAlert2At      DATETIME2(3) NULL,
    NextEventAt             DATETIME2(3) NULL,
    LeaseUntil              DATETIME2(3) NULL, -- a worker is processing this row until then
    RowVersion              ROWVERSION NOT NULL
);
CREATE INDEX IX_TicketSla_NextEvent ON dbo.TicketSla(NextEventAt) WHERE NextEventAt IS NOT NULL;

CREATE TABLE dbo.TicketEscalation
(
    TicketEscalationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketEscalation PRIMARY KEY,
    TicketId           BIGINT NOT NULL CONSTRAINT FK_TicketEscalation_Ticket REFERENCES dbo.Ticket(TicketId),
    Level              TINYINT NOT NULL,
    Reasons            VARCHAR(100) NOT NULL, -- comma-separated: first_response, next_response, resolution
    OwnerUserId        BIGINT NULL CONSTRAINT FK_TicketEscalation_Owner REFERENCES dbo.[User](UserId),
    StartedAt          DATETIME2(3) NOT NULL,
    WindowEndsAt       DATETIME2(3) NULL,
    LastFollowUpAt     DATETIME2(3) NULL,
    ClearedAt          DATETIME2(3) NULL,
    ClearedByUserId    BIGINT NULL CONSTRAINT FK_TicketEscalation_ClearedBy REFERENCES dbo.[User](UserId)
);
-- One active escalation per ticket.
CREATE UNIQUE INDEX UX_TicketEscalation_Active ON dbo.TicketEscalation(TicketId) WHERE ClearedAt IS NULL;
CREATE INDEX IX_TicketEscalation_Owner ON dbo.TicketEscalation(OwnerUserId) WHERE ClearedAt IS NULL;

ALTER TABLE dbo.ServiceDesk ADD AutoCloseHours INT NOT NULL CONSTRAINT DF_ServiceDesk_AutoClose DEFAULT (48);
GO

-- No job tables: scheduling lives in the independent TicketCore.Worker process and its config file.
-- The database only holds state (TicketSla, TicketEscalation) and configuration (calendars, SLA policies).

-- Where the engine has read up to in the TicketHistory event stream. Whoever holds the lease reads the stream;
-- if that worker dies, another takes over when the lease expires.
CREATE TABLE dbo.EngineCheckpoint
(
    Name          VARCHAR(50) NOT NULL CONSTRAINT PK_EngineCheckpoint PRIMARY KEY,
    LastEventId   BIGINT NOT NULL,
    LeaseOwner    VARCHAR(100) NULL,
    LeaseUntil    DATETIME2(3) NULL,
    UpdatedAt     DATETIME2(3) NULL
);
-- Start after existing history, so old tickets are picked up by the SLA backfill instead of replaying old events.
INSERT INTO dbo.EngineCheckpoint (Name, LastEventId) SELECT 'ticket-history', ISNULL(MAX(TicketHistoryId), 0) FROM dbo.TicketHistory;

-- ---------- Seed: calendars ----------
INSERT INTO dbo.Calendar (Name, TimeZoneId, Is24x7, IsDefault) VALUES
(N'Standard business hours', 'Asia/Kolkata', 0, 1),
(N'24x7', 'Asia/Kolkata', 1, 0);

-- Monday to Friday 09:00-18:00 local time.
INSERT INTO dbo.CalendarHours (CalendarId, DayOfWeek, StartMinute, EndMinute)
SELECT c.CalendarId, d.Day, 540, 1080
FROM dbo.Calendar c CROSS JOIN (VALUES (1),(2),(3),(4),(5)) d(Day)
WHERE c.Name = N'Standard business hours';

INSERT INTO dbo.HolidayList (Name) VALUES (N'India national holidays');
INSERT INTO dbo.Holiday (HolidayListId, HolidayDate, Name)
SELECT h.HolidayListId, v.HolidayDate, v.Name FROM dbo.HolidayList h
CROSS JOIN (VALUES ('2026-01-26', N'Republic Day'), ('2026-08-15', N'Independence Day'), ('2026-10-02', N'Gandhi Jayanti'),
                   ('2027-01-26', N'Republic Day'), ('2027-08-15', N'Independence Day'), ('2027-10-02', N'Gandhi Jayanti')) v(HolidayDate, Name)
WHERE h.Name = N'India national holidays';

INSERT INTO dbo.CalendarHolidayList (CalendarId, HolidayListId)
SELECT c.CalendarId, h.HolidayListId FROM dbo.Calendar c CROSS JOIN dbo.HolidayList h
WHERE c.Name = N'Standard business hours' AND h.Name = N'India national holidays';

-- ---------- Seed: default SLA policies (global, one per priority), in business minutes ----------
INSERT INTO dbo.SlaPolicy (Name, PriorityId, FirstResponseMinutes, NextResponseMinutes, ResolutionMinutes, ReminderMinutes,
                           EscalateOnFirstResponse, EscalateOnNextResponse, EscalateOnResolution) VALUES
(N'Default - Urgent', 1,  60,  120,  480,  15, 1, 0, 1),
(N'Default - High',   2, 120,  240,  960,  30, 1, 0, 1),
(N'Default - Medium', 3, 240,  480, 1440,  60, 0, 0, 1),
(N'Default - Low',    4, 480,  960, 2400, 120, 0, 0, 0);

-- Escalation path: Team Lead -> Head of Department -> Business Unit Head (final).
INSERT INTO dbo.EscalationLevel (SlaPolicyId, Level, RoleId, WindowMinutes)
SELECT p.SlaPolicyId, l.Level, r.RoleId,
       CASE l.Level WHEN 3 THEN NULL
            ELSE CASE p.PriorityId WHEN 1 THEN 30 WHEN 2 THEN 60 WHEN 3 THEN 120 ELSE 240 END * l.Level END
FROM dbo.SlaPolicy p
CROSS JOIN (VALUES (1, N'Team Lead'), (2, N'Head of Department'), (3, N'Business Unit Head')) l(Level, RoleName)
JOIN dbo.Role r ON r.Name = l.RoleName;
