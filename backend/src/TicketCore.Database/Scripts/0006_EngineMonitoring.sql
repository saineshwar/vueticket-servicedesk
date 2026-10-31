-- Engine monitoring: what each worker task did and when, so administrators can see it in the app.
-- Written by the worker itself after each run; nothing here is scheduled by the database.

-- Latest state of each task on each worker instance (one row per instance and task, updated every run).
CREATE TABLE dbo.EngineTaskState
(
    InstanceId      VARCHAR(100) NOT NULL,
    Task            VARCHAR(50)  NOT NULL,
    Enabled         BIT          NOT NULL,
    IntervalSeconds INT          NOT NULL,
    InstanceStartedAt DATETIME2(3) NOT NULL,
    LastRunAt       DATETIME2(3) NULL,
    LastDurationMs  INT          NULL,
    LastProcessed   INT          NOT NULL CONSTRAINT DF_EngineTaskState_Processed DEFAULT (0),
    LastSuccessAt   DATETIME2(3) NULL,
    LastErrorAt     DATETIME2(3) NULL,
    LastError       NVARCHAR(1000) NULL,
    Runs            BIGINT       NOT NULL CONSTRAINT DF_EngineTaskState_Runs DEFAULT (0),
    Failures        BIGINT       NOT NULL CONSTRAINT DF_EngineTaskState_Failures DEFAULT (0),
    CONSTRAINT PK_EngineTaskState PRIMARY KEY (InstanceId, Task)
);
GO

-- Runs that did something or failed. Idle runs only update EngineTaskState, to keep this log readable and small.
CREATE TABLE dbo.EngineRunLog
(
    EngineRunLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EngineRunLog PRIMARY KEY,
    InstanceId     VARCHAR(100) NOT NULL,
    Task           VARCHAR(50)  NOT NULL,
    StartedAt      DATETIME2(3) NOT NULL,
    DurationMs     INT          NOT NULL,
    Processed      INT          NOT NULL,
    Succeeded      BIT          NOT NULL,
    Error          NVARCHAR(1000) NULL
);
CREATE INDEX IX_EngineRunLog_StartedAt ON dbo.EngineRunLog(StartedAt DESC) INCLUDE (Task, Succeeded);
GO
