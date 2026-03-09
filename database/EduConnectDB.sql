-- ============================================================
--  EduConnect: Smart Campus Communication System
--  Database Creation Script
--  SQL Server Express
--  Version: 2.0 — Added UpdatedAt to relevant tables
-- ============================================================

-- STEP 1: Create the Database
CREATE DATABASE EduConnectDB;
GO

-- STEP 2: Use the Database
USE EduConnectDB;
GO

-- ============================================================
--  TABLE 1: Roles
--  UpdatedAt: YES — role permissions may change
-- ============================================================
CREATE TABLE Roles (
    RoleID          INT IDENTITY(1,1)   NOT NULL,
    RoleName        NVARCHAR(50)        NOT NULL,
    RoleLevel       INT                 NOT NULL,   -- 1=Student, 2=Faculty, 3=Admin
    Description     NVARCHAR(255)       NULL,
    CanPublish      BIT                 NOT NULL    DEFAULT 0,
    CanManageUsers  BIT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when permissions are changed

    CONSTRAINT PK_Roles PRIMARY KEY (RoleID),
    CONSTRAINT UQ_Roles_RoleName UNIQUE (RoleName)
);
GO

-- ============================================================
--  TABLE 2: Users
--  UpdatedAt: YES — profile, password, status can change
-- ============================================================
CREATE TABLE Users (
    UserID          INT IDENTITY(1,1)   NOT NULL,
    FirstName       NVARCHAR(100)       NOT NULL,
    LastName        NVARCHAR(100)       NOT NULL,
    Email           NVARCHAR(255)       NOT NULL,
    PasswordHash    NVARCHAR(512)       NOT NULL,
    StudentID       NVARCHAR(50)        NULL,
    Department      NVARCHAR(100)       NULL,
    RoleID          INT                 NOT NULL,
    IsActive        BIT                 NOT NULL    DEFAULT 1,
    ProfilePicture  NVARCHAR(500)       NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when profile is edited
    LastLogin       DATETIME            NULL,

    CONSTRAINT PK_Users PRIMARY KEY (UserID),
    CONSTRAINT UQ_Users_Email UNIQUE (Email),
    CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleID)
        REFERENCES Roles(RoleID)
);
GO

-- ============================================================
--  TABLE 3: AnnouncementCategories
--  UpdatedAt: YES — name, color, icon may be updated
-- ============================================================
CREATE TABLE AnnouncementCategories (
    CategoryID      INT IDENTITY(1,1)   NOT NULL,
    CategoryName    NVARCHAR(100)       NOT NULL,
    Description     NVARCHAR(255)       NULL,
    ColorHex        CHAR(7)             NOT NULL    DEFAULT '#000000',
    IconName        NVARCHAR(50)        NULL,
    IsEmergency     BIT                 NOT NULL    DEFAULT 0,
    IsActive        BIT                 NOT NULL    DEFAULT 1,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when category details change

    CONSTRAINT PK_AnnouncementCategories PRIMARY KEY (CategoryID),
    CONSTRAINT UQ_Categories_Name UNIQUE (CategoryName)
);
GO

-- ============================================================
--  TABLE 4: Announcements
--  UpdatedAt: YES — title, body, status, priority can change
-- ============================================================
CREATE TABLE Announcements (
    AnnouncementID  INT IDENTITY(1,1)   NOT NULL,
    AuthorID        INT                 NOT NULL,
    CategoryID      INT                 NOT NULL,
    Title           NVARCHAR(300)       NOT NULL,
    Body            NVARCHAR(MAX)       NOT NULL,
    AISummary       NVARCHAR(500)       NULL,
    Status          NVARCHAR(20)        NOT NULL    DEFAULT 'Draft',
    -- Status values: Draft, Published, Archived, Expired
    Priority        TINYINT             NOT NULL    DEFAULT 1,
    -- Priority: 1=Low, 2=Normal, 3=High, 4=Urgent, 5=Emergency
    TargetAudience  NVARCHAR(50)        NOT NULL    DEFAULT 'All',
    -- TargetAudience: All, Students, Faculty, Department
    AttachmentURL   NVARCHAR(500)       NULL,
    PublishedAt     DATETIME            NULL,
    ExpiresAt       DATETIME            NULL,
    IsEmergency     BIT                 NOT NULL    DEFAULT 0,
    ViewCount       INT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when announcement is edited

    CONSTRAINT PK_Announcements PRIMARY KEY (AnnouncementID),
    CONSTRAINT FK_Announcements_Users FOREIGN KEY (AuthorID)
        REFERENCES Users(UserID),
    CONSTRAINT FK_Announcements_Categories FOREIGN KEY (CategoryID)
        REFERENCES AnnouncementCategories(CategoryID),
    CONSTRAINT CHK_Announcements_Status CHECK (
        Status IN ('Draft', 'Published', 'Archived', 'Expired')
    ),
    CONSTRAINT CHK_Announcements_Priority CHECK (
        Priority BETWEEN 1 AND 5
    )
);
GO

-- ============================================================
--  TABLE 5: Notifications
--  UpdatedAt: NO — write-once, only IsRead changes
--  (IsRead + ReadAt already tracks the state change)
-- ============================================================
CREATE TABLE Notifications (
    NotificationID  INT IDENTITY(1,1)   NOT NULL,
    UserID          INT                 NOT NULL,
    AnnouncementID  INT                 NOT NULL,
    Message         NVARCHAR(500)       NOT NULL,
    IsRead          BIT                 NOT NULL    DEFAULT 0,
    ReadAt          DATETIME            NULL,
    Channel         NVARCHAR(20)        NOT NULL    DEFAULT 'InApp',
    -- Channel values: InApp, Email, Push
    SentAt          DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_Notifications PRIMARY KEY (NotificationID),
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID),
    CONSTRAINT FK_Notifications_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID),
    CONSTRAINT CHK_Notifications_Channel CHECK (
        Channel IN ('InApp', 'Email', 'Push')
    )
);
GO

-- ============================================================
--  TABLE 6: Feedback
--  UpdatedAt: YES — IsAcknowledged status changes
-- ============================================================
CREATE TABLE Feedback (
    FeedbackID      INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID  INT                 NOT NULL,
    UserID          INT                 NOT NULL,
    FeedbackText    NVARCHAR(1000)      NULL,
    Rating          TINYINT             NULL,
    SentimentScore  DECIMAL(4,3)        NULL,       -- AI: 0.000 to 1.000
    SentimentLabel  NVARCHAR(20)        NULL,       -- AI: Positive, Neutral, Negative
    IsAcknowledged  BIT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when acknowledged by admin

    CONSTRAINT PK_Feedback PRIMARY KEY (FeedbackID),
    CONSTRAINT FK_Feedback_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID),
    CONSTRAINT FK_Feedback_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID),
    CONSTRAINT CHK_Feedback_Rating CHECK (
        Rating BETWEEN 1 AND 5 OR Rating IS NULL
    ),
    CONSTRAINT CHK_Feedback_Sentiment CHECK (
        SentimentLabel IN ('Positive', 'Neutral', 'Negative') OR SentimentLabel IS NULL
    )
);
GO

-- ============================================================
--  TABLE 7: Events
--  UpdatedAt: YES — schedule, location, URL can change
-- ============================================================
CREATE TABLE Events (
    EventID         INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID  INT                 NULL,
    OrganizerID     INT                 NOT NULL,
    EventTitle      NVARCHAR(300)       NOT NULL,
    Location        NVARCHAR(255)       NULL,
    StartDateTime   DATETIME            NOT NULL,
    EndDateTime     DATETIME            NOT NULL,
    MaxAttendees    INT                 NULL,
    IsOnline        BIT                 NOT NULL    DEFAULT 0,
    MeetingURL      NVARCHAR(500)       NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,       -- set when event details change

    CONSTRAINT PK_Events PRIMARY KEY (EventID),
    CONSTRAINT FK_Events_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID),
    CONSTRAINT FK_Events_Users FOREIGN KEY (OrganizerID)
        REFERENCES Users(UserID),
    CONSTRAINT CHK_Events_Dates CHECK (
        EndDateTime > StartDateTime
    )
);
GO

-- ============================================================
--  TABLE 8: AIProcessingLogs
--  UpdatedAt: NO — AI logs are permanent records
-- ============================================================
CREATE TABLE AIProcessingLogs (
    AILogID         INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID  INT                 NULL,
    ProcessType     NVARCHAR(50)        NOT NULL,
    -- ProcessType: Summarization, Categorization, Sentiment, Chatbot
    InputText       NVARCHAR(MAX)       NULL,
    OutputText      NVARCHAR(MAX)       NULL,
    ModelUsed       NVARCHAR(100)       NULL,
    TokensUsed      INT                 NULL,
    Confidence      DECIMAL(5,4)        NULL,
    ProcessedAt     DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_AIProcessingLogs PRIMARY KEY (AILogID),
    CONSTRAINT FK_AILogs_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID)
);
GO

-- ============================================================
--  TABLE 9: ChatbotConversations
--  UpdatedAt: NO — chat history is permanent
-- ============================================================
CREATE TABLE ChatbotConversations (
    ConversationID  INT IDENTITY(1,1)   NOT NULL,
    UserID          INT                 NOT NULL,
    SessionToken    NVARCHAR(255)       NOT NULL,
    UserMessage     NVARCHAR(MAX)       NOT NULL,
    BotResponse     NVARCHAR(MAX)       NOT NULL,
    IntentDetected  NVARCHAR(100)       NULL,
    WasHelpful      BIT                 NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_ChatbotConversations PRIMARY KEY (ConversationID),
    CONSTRAINT FK_Chatbot_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID)
);
GO

-- ============================================================
--  TABLE 10: RefreshTokens
--  UpdatedAt: NO — tokens are revoked not edited
--  (IsRevoked already tracks the state change)
-- ============================================================
CREATE TABLE RefreshTokens (
    TokenID         INT IDENTITY(1,1)   NOT NULL,
    UserID          INT                 NOT NULL,
    Token           NVARCHAR(512)       NOT NULL,
    ExpiresAt       DATETIME            NOT NULL,
    IsRevoked       BIT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    DeviceInfo      NVARCHAR(255)       NULL,

    CONSTRAINT PK_RefreshTokens PRIMARY KEY (TokenID),
    CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID)
);
GO

-- ============================================================
--  TABLE 11: AuditLogs
--  UpdatedAt: NO — audit logs are immutable by design
-- ============================================================
CREATE TABLE AuditLogs (
    LogID           BIGINT IDENTITY(1,1) NOT NULL,
    UserID          INT                 NULL,
    Action          NVARCHAR(100)       NOT NULL,
    TableAffected   NVARCHAR(100)       NOT NULL,
    RecordID        INT                 NULL,
    OldValues       NVARCHAR(MAX)       NULL,
    NewValues       NVARCHAR(MAX)       NULL,
    IPAddress       NVARCHAR(45)        NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_AuditLogs PRIMARY KEY (LogID),
    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID)
);
GO

-- ============================================================
--  INDEXES (for performance)
-- ============================================================
CREATE INDEX IX_Users_Email              ON Users(Email);
CREATE INDEX IX_Users_RoleID             ON Users(RoleID);
CREATE INDEX IX_Announcements_Status     ON Announcements(Status);
CREATE INDEX IX_Announcements_Author     ON Announcements(AuthorID);
CREATE INDEX IX_Announcements_UpdatedAt  ON Announcements(UpdatedAt);
CREATE INDEX IX_Notifications_UserID     ON Notifications(UserID);
CREATE INDEX IX_Notifications_IsRead     ON Notifications(IsRead);
CREATE INDEX IX_Feedback_Announcement    ON Feedback(AnnouncementID);
CREATE INDEX IX_AuditLogs_UserID         ON AuditLogs(UserID);
CREATE INDEX IX_AuditLogs_CreatedAt      ON AuditLogs(CreatedAt);
GO

-- ============================================================
--  SEED DATA: Default Roles
-- ============================================================
INSERT INTO Roles (RoleName, RoleLevel, Description, CanPublish, CanManageUsers)
VALUES
    ('Student',       1, 'Basic user - can view announcements and give feedback', 0, 0),
    ('Faculty',       2, 'Intermediate user - can publish announcements',          1, 0),
    ('Administrator', 3, 'Advanced user - full system access',                    1, 1);
GO

-- ============================================================
--  SEED DATA: Default Categories
-- ============================================================
INSERT INTO AnnouncementCategories (CategoryName, Description, ColorHex, IconName, IsEmergency)
VALUES
    ('Academic',        'Class schedules, exams, grades',        '#3B82F6', 'fa-book',        0),
    ('Extracurricular', 'Clubs, sports, events',                 '#10B981', 'fa-star',        0),
    ('Administrative',  'School policies, office announcements', '#8B5CF6', 'fa-building',    0),
    ('Emergency',       'Urgent campus-wide alerts',             '#EF4444', 'fa-exclamation', 1),
    ('General',         'General campus information',            '#64748B', 'fa-info-circle', 0);
GO

-- ============================================================
--  SEED DATA: Default Admin User
--  IMPORTANT: Replace PasswordHash after ASP.NET setup
-- ============================================================
INSERT INTO Users (FirstName, LastName, Email, PasswordHash, RoleID, IsActive)
VALUES
    ('System', 'Administrator', 'admin@educonnect.edu',
     'REPLACE_WITH_BCRYPT_HASH', 3, 1);
GO

-- ============================================================
--  VERIFY: Check all tables were created
-- ============================================================
SELECT
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_CATALOG = 'EduConnectDB'
ORDER BY TABLE_NAME;
GO

-- ============================================================
--  VERIFY: Check UpdatedAt columns exist on correct tables
-- ============================================================
SELECT
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE
    TABLE_CATALOG = 'EduConnectDB'
    AND COLUMN_NAME IN ('CreatedAt', 'UpdatedAt')
ORDER BY TABLE_NAME, COLUMN_NAME;
GO

PRINT '============================================================';
PRINT ' EduConnectDB v2.0 created successfully!';
PRINT ' Tables: 11 | Indexes: 10 | UpdatedAt: 5 tables';
PRINT ' Roles + Categories + Admin user seeded';
PRINT '============================================================';