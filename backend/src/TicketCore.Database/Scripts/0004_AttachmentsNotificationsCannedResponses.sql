-- 0004: Attachments, delete/restore fields, saved replies, in-app notifications, outgoing email queue.

ALTER TABLE dbo.Ticket ADD
    DeletedAt       DATETIME2(3) NULL,
    DeletedByUserId BIGINT NULL CONSTRAINT FK_Ticket_DeletedBy REFERENCES dbo.[User](UserId),
    DeleteReason    NVARCHAR(500) NULL;
GO

CREATE INDEX IX_Ticket_Deleted ON dbo.Ticket(TeamId, DeletedAt) WHERE IsDeleted = 1;

-- Files live in storage (disk or S3); only metadata is in the database.
CREATE TABLE dbo.Attachment
(
    AttachmentId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachment PRIMARY KEY,
    TicketId         BIGINT NOT NULL CONSTRAINT FK_Attachment_Ticket REFERENCES dbo.Ticket(TicketId),
    TicketCommentId  BIGINT NULL CONSTRAINT FK_Attachment_Comment REFERENCES dbo.TicketComment(TicketCommentId),
    IsInternal       BIT NOT NULL,
    FileName         NVARCHAR(255) NOT NULL,
    ContentType      VARCHAR(100) NOT NULL,
    SizeBytes        BIGINT NOT NULL,
    StorageKey       VARCHAR(200) NOT NULL,
    UploadedByUserId BIGINT NOT NULL CONSTRAINT FK_Attachment_UploadedBy REFERENCES dbo.[User](UserId),
    UploadedAt       DATETIME2(3) NOT NULL,
    CONSTRAINT UQ_Attachment_StorageKey UNIQUE (StorageKey)
);
CREATE INDEX IX_Attachment_Ticket ON dbo.Attachment(TicketId);

-- Saved replies: personal (OwnerUserId) or shared with a team (TeamId).
CREATE TABLE dbo.CannedResponse
(
    CannedResponseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CannedResponse PRIMARY KEY,
    Title            NVARCHAR(100) NOT NULL,
    Body             NVARCHAR(4000) NOT NULL,
    OwnerUserId      BIGINT NULL CONSTRAINT FK_CannedResponse_Owner REFERENCES dbo.[User](UserId),
    TeamId           INT NULL CONSTRAINT FK_CannedResponse_Team REFERENCES dbo.OrgUnit(OrgUnitId),
    CreatedByUserId  BIGINT NOT NULL CONSTRAINT FK_CannedResponse_CreatedBy REFERENCES dbo.[User](UserId),
    CreatedAt        DATETIME2(3) NOT NULL,
    UpdatedAt        DATETIME2(3) NULL,
    CONSTRAINT CK_CannedResponse_Scope CHECK ((OwnerUserId IS NULL AND TeamId IS NOT NULL) OR (OwnerUserId IS NOT NULL AND TeamId IS NULL))
);
CREATE INDEX IX_CannedResponse_Owner ON dbo.CannedResponse(OwnerUserId);
CREATE INDEX IX_CannedResponse_Team ON dbo.CannedResponse(TeamId);

CREATE TABLE dbo.Notification
(
    NotificationId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notification PRIMARY KEY,
    RecipientUserId BIGINT NOT NULL CONSTRAINT FK_Notification_Recipient REFERENCES dbo.[User](UserId),
    TicketId        BIGINT NULL CONSTRAINT FK_Notification_Ticket REFERENCES dbo.Ticket(TicketId),
    Type            VARCHAR(50) NOT NULL,
    Title           NVARCHAR(300) NOT NULL,
    CreatedAt       DATETIME2(3) NOT NULL,
    ReadAt          DATETIME2(3) NULL
);
CREATE INDEX IX_Notification_Recipient ON dbo.Notification(RecipientUserId, ReadAt, CreatedAt);

-- Outgoing email, sent by a background worker with retries.
CREATE TABLE dbo.EmailQueue
(
    EmailId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailQueue PRIMARY KEY,
    ToAddress      NVARCHAR(256) NOT NULL,
    ToName         NVARCHAR(200) NULL,
    Subject        NVARCHAR(300) NOT NULL,
    BodyText       NVARCHAR(MAX) NOT NULL,
    BodyHtml       NVARCHAR(MAX) NOT NULL,
    TicketId       BIGINT NULL CONSTRAINT FK_EmailQueue_Ticket REFERENCES dbo.Ticket(TicketId),
    CreatedAt      DATETIME2(3) NOT NULL,
    NextAttemptAt  DATETIME2(3) NOT NULL,
    Attempts       INT NOT NULL CONSTRAINT DF_EmailQueue_Attempts DEFAULT (0),
    SentAt         DATETIME2(3) NULL,
    FailedAt       DATETIME2(3) NULL,
    LastError      NVARCHAR(1000) NULL
);
CREATE INDEX IX_EmailQueue_Pending ON dbo.EmailQueue(NextAttemptAt) WHERE SentAt IS NULL AND FailedAt IS NULL;
