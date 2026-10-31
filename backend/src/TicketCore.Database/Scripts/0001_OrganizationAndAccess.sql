-- 0001: Organization tree, users, roles, permissions, role assignments, refresh tokens, audit log.

CREATE TABLE dbo.OrgUnit
(
    OrgUnitId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrgUnit PRIMARY KEY,
    ParentId     INT NULL CONSTRAINT FK_OrgUnit_Parent REFERENCES dbo.OrgUnit(OrgUnitId),
    UnitType     TINYINT NOT NULL CONSTRAINT CK_OrgUnit_UnitType CHECK (UnitType BETWEEN 1 AND 4),
    Name         NVARCHAR(150) NOT NULL,
    Code         VARCHAR(30) NOT NULL,
    IsActive     BIT NOT NULL CONSTRAINT DF_OrgUnit_IsActive DEFAULT (1),
    CreatedAt    DATETIME2(3) NOT NULL CONSTRAINT DF_OrgUnit_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt    DATETIME2(3) NULL,
    RowVersion   ROWVERSION NOT NULL,
    CONSTRAINT UQ_OrgUnit_Code UNIQUE (Code),
    CONSTRAINT CK_OrgUnit_RootIsOrganization CHECK ((ParentId IS NULL AND UnitType = 1) OR (ParentId IS NOT NULL AND UnitType > 1))
);
CREATE INDEX IX_OrgUnit_ParentId ON dbo.OrgUnit(ParentId);
-- Only one root.
CREATE UNIQUE INDEX UX_OrgUnit_SingleRoot ON dbo.OrgUnit(UnitType) WHERE ParentId IS NULL;

-- Closure table: one row per (ancestor, descendant) pair, including each unit with itself at depth 0.
CREATE TABLE dbo.OrgUnitClosure
(
    AncestorId   INT NOT NULL CONSTRAINT FK_OrgUnitClosure_Ancestor REFERENCES dbo.OrgUnit(OrgUnitId),
    DescendantId INT NOT NULL CONSTRAINT FK_OrgUnitClosure_Descendant REFERENCES dbo.OrgUnit(OrgUnitId),
    Depth        INT NOT NULL,
    CONSTRAINT PK_OrgUnitClosure PRIMARY KEY (AncestorId, DescendantId)
);
CREATE INDEX IX_OrgUnitClosure_Descendant ON dbo.OrgUnitClosure(DescendantId) INCLUDE (AncestorId, Depth);

CREATE TABLE dbo.[User]
(
    UserId              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_User PRIMARY KEY,
    UserName            NVARCHAR(100) NOT NULL,
    Email               NVARCHAR(256) NOT NULL,
    FirstName           NVARCHAR(100) NULL,
    LastName            NVARCHAR(100) NULL,
    Mobile              VARCHAR(20) NULL,
    UserType            TINYINT NOT NULL CONSTRAINT CK_User_UserType CHECK (UserType BETWEEN 1 AND 3),
    HomeOrgUnitId       INT NULL CONSTRAINT FK_User_HomeOrgUnit REFERENCES dbo.OrgUnit(OrgUnitId),
    ManagerUserId       BIGINT NULL CONSTRAINT FK_User_Manager REFERENCES dbo.[User](UserId),
    PasswordHash        NVARCHAR(400) NULL,
    LegacyPasswordHash  VARCHAR(64) NULL,
    IsActive            BIT NOT NULL CONSTRAINT DF_User_IsActive DEFAULT (1),
    EmailVerified       BIT NOT NULL CONSTRAINT DF_User_EmailVerified DEFAULT (0),
    FailedLoginCount    INT NOT NULL CONSTRAINT DF_User_FailedLoginCount DEFAULT (0),
    LockoutEndAt        DATETIME2(3) NULL,
    SecurityStamp       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_User_SecurityStamp DEFAULT (NEWID()),
    ExternalId          NVARCHAR(200) NULL,
    LastLoginAt         DATETIME2(3) NULL,
    CreatedAt           DATETIME2(3) NOT NULL CONSTRAINT DF_User_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt           DATETIME2(3) NULL,
    RowVersion          ROWVERSION NOT NULL,
    CONSTRAINT UQ_User_UserName UNIQUE (UserName),
    CONSTRAINT UQ_User_Email UNIQUE (Email)
);
CREATE INDEX IX_User_HomeOrgUnitId ON dbo.[User](HomeOrgUnitId);
CREATE INDEX IX_User_ManagerUserId ON dbo.[User](ManagerUserId);

CREATE TABLE dbo.Permission
(
    PermissionKey VARCHAR(64) NOT NULL CONSTRAINT PK_Permission PRIMARY KEY,
    GroupName     NVARCHAR(50) NOT NULL,
    Description   NVARCHAR(200) NOT NULL
);

CREATE TABLE dbo.Role
(
    RoleId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL,
    Description  NVARCHAR(500) NULL,
    IsSystem     BIT NOT NULL CONSTRAINT DF_Role_IsSystem DEFAULT (0),
    CreatedAt    DATETIME2(3) NOT NULL CONSTRAINT DF_Role_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt    DATETIME2(3) NULL,
    RowVersion   ROWVERSION NOT NULL,
    CONSTRAINT UQ_Role_Name UNIQUE (Name)
);

CREATE TABLE dbo.RolePermission
(
    RoleId        INT NOT NULL CONSTRAINT FK_RolePermission_Role REFERENCES dbo.Role(RoleId) ON DELETE CASCADE,
    PermissionKey VARCHAR(64) NOT NULL CONSTRAINT FK_RolePermission_Permission REFERENCES dbo.Permission(PermissionKey),
    CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionKey)
);

CREATE TABLE dbo.RoleAssignment
(
    RoleAssignmentId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoleAssignment PRIMARY KEY,
    UserId             BIGINT NOT NULL CONSTRAINT FK_RoleAssignment_User REFERENCES dbo.[User](UserId),
    RoleId             INT NOT NULL CONSTRAINT FK_RoleAssignment_Role REFERENCES dbo.Role(RoleId),
    OrgUnitId          INT NOT NULL CONSTRAINT FK_RoleAssignment_OrgUnit REFERENCES dbo.OrgUnit(OrgUnitId),
    IncludeDescendants BIT NOT NULL CONSTRAINT DF_RoleAssignment_IncludeDescendants DEFAULT (1),
    CreatedAt          DATETIME2(3) NOT NULL CONSTRAINT DF_RoleAssignment_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy          BIGINT NULL CONSTRAINT FK_RoleAssignment_CreatedBy REFERENCES dbo.[User](UserId),
    CONSTRAINT UQ_RoleAssignment UNIQUE (UserId, RoleId, OrgUnitId)
);
CREATE INDEX IX_RoleAssignment_OrgUnitId ON dbo.RoleAssignment(OrgUnitId);
CREATE INDEX IX_RoleAssignment_RoleId ON dbo.RoleAssignment(RoleId);

CREATE TABLE dbo.RefreshToken
(
    RefreshTokenId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshToken PRIMARY KEY,
    UserId              BIGINT NOT NULL CONSTRAINT FK_RefreshToken_User REFERENCES dbo.[User](UserId),
    TokenHash           CHAR(64) NOT NULL,
    FamilyId            UNIQUEIDENTIFIER NOT NULL,
    ExpiresAt           DATETIME2(3) NOT NULL,
    CreatedAt           DATETIME2(3) NOT NULL,
    CreatedByIp         VARCHAR(64) NULL,
    RevokedAt           DATETIME2(3) NULL,
    ReplacedByTokenHash CHAR(64) NULL,
    CONSTRAINT UQ_RefreshToken_TokenHash UNIQUE (TokenHash)
);
CREATE INDEX IX_RefreshToken_User ON dbo.RefreshToken(UserId);
CREATE INDEX IX_RefreshToken_Family ON dbo.RefreshToken(FamilyId);

CREATE TABLE dbo.AuditLog
(
    AuditLogId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    OccurredAt  DATETIME2(3) NOT NULL,
    UserId      BIGINT NULL,
    Action      VARCHAR(100) NOT NULL,
    EntityType  VARCHAR(100) NULL,
    EntityId    VARCHAR(100) NULL,
    Details     NVARCHAR(MAX) NULL,
    IpAddress   VARCHAR(64) NULL
);
CREATE INDEX IX_AuditLog_OccurredAt ON dbo.AuditLog(OccurredAt);
CREATE INDEX IX_AuditLog_Entity ON dbo.AuditLog(EntityType, EntityId);
