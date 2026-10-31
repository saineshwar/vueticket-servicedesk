-- 0003: Priorities, ticket types, service desks, categories, tickets, comments, history.

CREATE TABLE dbo.Priority
(
    PriorityId TINYINT NOT NULL CONSTRAINT PK_Priority PRIMARY KEY,
    Name       NVARCHAR(30) NOT NULL,
    SortOrder  TINYINT NOT NULL
);
INSERT INTO dbo.Priority (PriorityId, Name, SortOrder) VALUES (1, N'Urgent', 1), (2, N'High', 2), (3, N'Medium', 3), (4, N'Low', 4);

CREATE TABLE dbo.TicketType
(
    TicketTypeId TINYINT NOT NULL CONSTRAINT PK_TicketType PRIMARY KEY,
    Name         NVARCHAR(50) NOT NULL
);
INSERT INTO dbo.TicketType (TicketTypeId, Name) VALUES (1, N'Incident'), (2, N'Service request'), (3, N'Question'), (4, N'Complaint');

-- A service desk is a front door (IT Help, HR Help, Customer Support). Its code prefixes tracking IDs.
CREATE TABLE dbo.ServiceDesk
(
    ServiceDeskId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ServiceDesk PRIMARY KEY,
    Name           NVARCHAR(100) NOT NULL,
    Code           VARCHAR(10) NOT NULL,
    Description    NVARCHAR(500) NULL,
    Audience       TINYINT NOT NULL CONSTRAINT CK_ServiceDesk_Audience CHECK (Audience BETWEEN 1 AND 3), -- 1 Employees, 2 Customers, 3 Both
    DefaultTeamId  INT NOT NULL CONSTRAINT FK_ServiceDesk_DefaultTeam REFERENCES dbo.OrgUnit(OrgUnitId),
    IsActive       BIT NOT NULL CONSTRAINT DF_ServiceDesk_IsActive DEFAULT (1),
    CreatedAt      DATETIME2(3) NOT NULL CONSTRAINT DF_ServiceDesk_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt      DATETIME2(3) NULL,
    RowVersion     ROWVERSION NOT NULL,
    CONSTRAINT UQ_ServiceDesk_Code UNIQUE (Code),
    CONSTRAINT UQ_ServiceDesk_Name UNIQUE (Name)
);

-- A category is what the requester picks; it decides the team (falls back to the desk's default team).
CREATE TABLE dbo.Category
(
    CategoryId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Category PRIMARY KEY,
    ServiceDeskId     INT NOT NULL CONSTRAINT FK_Category_ServiceDesk REFERENCES dbo.ServiceDesk(ServiceDeskId),
    Name              NVARCHAR(100) NOT NULL,
    Description       NVARCHAR(500) NULL,
    TeamId            INT NULL CONSTRAINT FK_Category_Team REFERENCES dbo.OrgUnit(OrgUnitId),
    TicketTypeId      TINYINT NOT NULL CONSTRAINT FK_Category_TicketType REFERENCES dbo.TicketType(TicketTypeId),
    DefaultPriorityId TINYINT NOT NULL CONSTRAINT FK_Category_Priority REFERENCES dbo.Priority(PriorityId),
    SortOrder         INT NOT NULL CONSTRAINT DF_Category_SortOrder DEFAULT (0),
    IsActive          BIT NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT (1),
    CONSTRAINT UQ_Category_Name UNIQUE (ServiceDeskId, Name)
);

CREATE SEQUENCE dbo.TicketNumber AS BIGINT START WITH 1001 INCREMENT BY 1;

CREATE TABLE dbo.Ticket
(
    TicketId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Ticket PRIMARY KEY,
    TrackingId        VARCHAR(30) NOT NULL,
    ServiceDeskId     INT NOT NULL CONSTRAINT FK_Ticket_ServiceDesk REFERENCES dbo.ServiceDesk(ServiceDeskId),
    CategoryId        INT NOT NULL CONSTRAINT FK_Ticket_Category REFERENCES dbo.Category(CategoryId),
    TicketTypeId      TINYINT NOT NULL CONSTRAINT FK_Ticket_TicketType REFERENCES dbo.TicketType(TicketTypeId),
    TeamId            INT NOT NULL CONSTRAINT FK_Ticket_Team REFERENCES dbo.OrgUnit(OrgUnitId),
    Subject           NVARCHAR(200) NOT NULL,
    Description       NVARCHAR(MAX) NOT NULL,
    Status            TINYINT NOT NULL CONSTRAINT CK_Ticket_Status CHECK (Status BETWEEN 1 AND 8),
    PriorityId        TINYINT NOT NULL CONSTRAINT FK_Ticket_Priority REFERENCES dbo.Priority(PriorityId),
    RequesterUserId   BIGINT NOT NULL CONSTRAINT FK_Ticket_Requester REFERENCES dbo.[User](UserId),
    CreatedByUserId   BIGINT NOT NULL CONSTRAINT FK_Ticket_CreatedBy REFERENCES dbo.[User](UserId),
    AssigneeUserId    BIGINT NULL CONSTRAINT FK_Ticket_Assignee REFERENCES dbo.[User](UserId),
    Channel           TINYINT NOT NULL, -- 1 Portal, 2 Agent, 3 Email
    CreatedAt         DATETIME2(3) NOT NULL,
    UpdatedAt         DATETIME2(3) NOT NULL,
    FirstRespondedAt  DATETIME2(3) NULL,
    ResolvedAt        DATETIME2(3) NULL,
    ClosedAt          DATETIME2(3) NULL,
    IsDeleted         BIT NOT NULL CONSTRAINT DF_Ticket_IsDeleted DEFAULT (0),
    RowVersion        ROWVERSION NOT NULL,
    CONSTRAINT UQ_Ticket_TrackingId UNIQUE (TrackingId)
);
CREATE INDEX IX_Ticket_TeamQueue ON dbo.Ticket(TeamId, Status, PriorityId, CreatedAt) WHERE IsDeleted = 0;
CREATE INDEX IX_Ticket_Assignee ON dbo.Ticket(AssigneeUserId, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_Ticket_Requester ON dbo.Ticket(RequesterUserId, CreatedAt) WHERE IsDeleted = 0;

-- Public replies and internal notes. Internal comments are never returned to requesters.
CREATE TABLE dbo.TicketComment
(
    TicketCommentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketComment PRIMARY KEY,
    TicketId        BIGINT NOT NULL CONSTRAINT FK_TicketComment_Ticket REFERENCES dbo.Ticket(TicketId),
    AuthorUserId    BIGINT NOT NULL CONSTRAINT FK_TicketComment_Author REFERENCES dbo.[User](UserId),
    Body            NVARCHAR(MAX) NOT NULL,
    IsInternal      BIT NOT NULL,
    AuthorIsSupport BIT NOT NULL,
    CreatedAt       DATETIME2(3) NOT NULL
);
CREATE INDEX IX_TicketComment_Ticket ON dbo.TicketComment(TicketId, CreatedAt);

CREATE TABLE dbo.TicketHistory
(
    TicketHistoryId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketHistory PRIMARY KEY,
    TicketId        BIGINT NOT NULL CONSTRAINT FK_TicketHistory_Ticket REFERENCES dbo.Ticket(TicketId),
    UserId          BIGINT NULL CONSTRAINT FK_TicketHistory_User REFERENCES dbo.[User](UserId),
    Activity        VARCHAR(50) NOT NULL,
    FromValue       NVARCHAR(200) NULL,
    ToValue         NVARCHAR(200) NULL,
    OccurredAt      DATETIME2(3) NOT NULL
);
CREATE INDEX IX_TicketHistory_Ticket ON dbo.TicketHistory(TicketId, OccurredAt);
