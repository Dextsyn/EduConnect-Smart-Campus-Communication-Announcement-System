-- ============================================================
--  EduConnect: Smart Campus Communication System
--  Database Creation Script
--  SQL Server Express
--  Version: 4.0
-- ============================================================

CREATE DATABASE EduConnectDB;
GO

USE EduConnectDB;
GO

-- ============================================================
--  TABLE 1: Roles
--  Lookup table for user roles
--  No dependencies
-- ============================================================
CREATE TABLE Roles (
    RoleID          INT IDENTITY(1,1)   NOT NULL,
    RoleName        NVARCHAR(50)        NOT NULL,
    RoleLevel       INT                 NOT NULL,
    -- 1=Student, 2=Faculty, 3=Staff, 4=Administrator
    Description     NVARCHAR(255)       NULL,
    CanPublish      BIT                 NOT NULL    DEFAULT 0,
    CanManageUsers  BIT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,

    CONSTRAINT PK_Roles PRIMARY KEY (RoleID),
    CONSTRAINT UQ_Roles_RoleName UNIQUE (RoleName)
);
GO

-- ============================================================
--  TABLE 2: TagTypes
--  NEW — Lookup table for DepartmentTag types
--  Normalizes: Academic, NonAcademic, Emergency
--  No dependencies
-- ============================================================
CREATE TABLE TagTypes (
    TagTypeID       INT IDENTITY(1,1)   NOT NULL,
    TypeName        NVARCHAR(50)        NOT NULL,
    Description     NVARCHAR(255)       NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_TagTypes PRIMARY KEY (TagTypeID),
    CONSTRAINT UQ_TagTypes_TypeName UNIQUE (TypeName)
);
GO

-- ============================================================
--  TABLE 3: DepartmentTags
--  All colleges and offices in Adamson University
--  Depends on: TagTypes
-- ============================================================
CREATE TABLE DepartmentTags (
    TagID           INT IDENTITY(1,1)   NOT NULL,
    TagName         NVARCHAR(50)       NOT NULL,
    ShortName       NVARCHAR(20)        NULL,
    TagTypeID       INT                 NOT NULL,   -- FK to TagTypes
    Description     NVARCHAR(255)       NULL,
    ColorHex        CHAR(7)             NOT NULL    DEFAULT '#3B82F6',
    IsActive        BIT                 NOT NULL    DEFAULT 1,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,

    CONSTRAINT PK_DepartmentTags PRIMARY KEY (TagID),
    CONSTRAINT UQ_DepartmentTags_Name UNIQUE (TagName),
    CONSTRAINT FK_DepartmentTags_TagTypes FOREIGN KEY (TagTypeID)
        REFERENCES TagTypes(TagTypeID)
);
GO

-- ============================================================
--  TABLE 4: Users
--  Depends on: Roles
--  DepartmentTagID removed — handled by UserDepartments
-- ============================================================
CREATE TABLE Users (
    UserID          INT IDENTITY(1,1)   NOT NULL,
    FirstName       NVARCHAR(50)       NOT NULL,
    LastName        NVARCHAR(50)       NOT NULL,
    Email           NVARCHAR(100)       NOT NULL,
    PasswordHash    NVARCHAR(512)       NOT NULL,
    StudentID       NVARCHAR(50)        NULL,
    RoleID          INT                 NOT NULL,
    IsActive        BIT                 NOT NULL    DEFAULT 1,
    ProfilePicture  NVARCHAR(500)       NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,
    LastLogin       DATETIME            NULL,

    CONSTRAINT PK_Users PRIMARY KEY (UserID),
    CONSTRAINT UQ_Users_Email UNIQUE (Email),
    CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleID)
        REFERENCES Roles(RoleID)
);
GO

-- ============================================================
--  TABLE 5: UserDepartments (Junction Table)
--  NEW — Links users to one or more departments
--  Depends on: Users, DepartmentTags
--  Replaces: Users.DepartmentTagID
-- ============================================================
CREATE TABLE UserDepartments (
    UserDepartmentID    INT IDENTITY(1,1)   NOT NULL,
    UserID              INT                 NOT NULL,
    TagID               INT                 NOT NULL,
    IsPrimary           BIT                 NOT NULL    DEFAULT 1,
    -- IsPrimary: marks the student's main department
    CreatedAt           DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_UserDepartments PRIMARY KEY (UserDepartmentID),
    CONSTRAINT FK_UserDepartments_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID),
    CONSTRAINT FK_UserDepartments_Tags FOREIGN KEY (TagID)
        REFERENCES DepartmentTags(TagID),
    -- Prevent duplicate department assignments per user
    CONSTRAINT UQ_UserDepartments UNIQUE (UserID, TagID)
);
GO

-- ============================================================
--  TABLE 6: AnnouncementCategories
--  General classification of announcements
--  No dependencies
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
    UpdatedAt       DATETIME            NULL,

    CONSTRAINT PK_AnnouncementCategories PRIMARY KEY (CategoryID),
    CONSTRAINT UQ_Categories_Name UNIQUE (CategoryName)
);
GO

-- ============================================================
--  TABLE 7: Announcements
--  Depends on: Users, AnnouncementCategories
--  TargetAudience REMOVED — handled by AnnouncementTags
--  FeedType added: Academic, NonAcademic, Emergency
-- ============================================================
CREATE TABLE Announcements (
    AnnouncementID  INT IDENTITY(1,1)   NOT NULL,
    AuthorID        INT                 NOT NULL,
    CategoryID      INT                 NOT NULL,
    FeedType        NVARCHAR(20)        NOT NULL    DEFAULT 'Academic',
    -- FeedType: Academic, NonAcademic, Emergency
    Title           NVARCHAR(150)       NOT NULL,
    Body            NVARCHAR(MAX)       NOT NULL,
    AISummary       NVARCHAR(255)       NULL,
    Status          NVARCHAR(20)        NOT NULL    DEFAULT 'Draft',
    -- Status: Draft, Published, Archived, Expired
    Priority        TINYINT             NOT NULL    DEFAULT 1,
    -- Priority: 1=Low, 2=Normal, 3=High, 4=Urgent, 5=Emergency
    AttachmentURL   NVARCHAR(500)       NULL,
    PublishedAt     DATETIME            NULL,
    ExpiresAt       DATETIME            NULL,
    IsEmergency     BIT                 NOT NULL    DEFAULT 0,
    ViewCount       INT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,

    CONSTRAINT PK_Announcements PRIMARY KEY (AnnouncementID),
    CONSTRAINT FK_Announcements_Users FOREIGN KEY (AuthorID)
        REFERENCES Users(UserID),
    CONSTRAINT FK_Announcements_Categories FOREIGN KEY (CategoryID)
        REFERENCES AnnouncementCategories(CategoryID),
    CONSTRAINT CHK_Announcements_FeedType CHECK (
        FeedType IN ('Academic', 'NonAcademic', 'Emergency')
    ),
    CONSTRAINT CHK_Announcements_Status CHECK (
        Status IN ('Draft', 'Published', 'Archived', 'Expired')
    ),
    CONSTRAINT CHK_Announcements_Priority CHECK (
        Priority BETWEEN 1 AND 5
    )
);
GO

-- ============================================================
--  TABLE 8: AnnouncementTags (Junction Table)
--  Links announcements to one or more DepartmentTags
--  Depends on: Announcements, DepartmentTags
-- ============================================================
CREATE TABLE AnnouncementTags (
    AnnouncementTagID   INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID      INT                 NOT NULL,
    TagID               INT                 NOT NULL,
    CreatedAt           DATETIME            NOT NULL    DEFAULT GETDATE(),

    CONSTRAINT PK_AnnouncementTags PRIMARY KEY (AnnouncementTagID),
    CONSTRAINT FK_AnnouncementTags_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID),
    CONSTRAINT FK_AnnouncementTags_Tags FOREIGN KEY (TagID)
        REFERENCES DepartmentTags(TagID),
    CONSTRAINT UQ_AnnouncementTags UNIQUE (AnnouncementID, TagID)
);
GO

-- ============================================================
--  TABLE 9: Notifications
--  Depends on: Users, Announcements
-- ============================================================
CREATE TABLE Notifications (
    NotificationID  INT IDENTITY(1,1)   NOT NULL,
    UserID          INT                 NOT NULL,
    AnnouncementID  INT                 NOT NULL,
    Message         NVARCHAR(500)       NOT NULL,
    IsRead          BIT                 NOT NULL    DEFAULT 0,
    ReadAt          DATETIME            NULL,
    Channel         NVARCHAR(20)        NOT NULL    DEFAULT 'InApp',
    -- Channel: InApp, Email, Push
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
--  TABLE 10: Feedback
--  Depends on: Users, Announcements
-- ============================================================
CREATE TABLE Feedback (
    FeedbackID      INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID  INT                 NOT NULL,
    UserID          INT                 NOT NULL,
    FeedbackText    NVARCHAR(500)      NULL,
    Rating          TINYINT             NULL,
    -- Rating: 1 to 5 stars
    SentimentScore  DECIMAL(4,3)        NULL,
    -- AI generated: 0.000 to 1.000
    SentimentLabel  NVARCHAR(20)        NULL,
    -- AI generated: Positive, Neutral, Negative
    IsAcknowledged  BIT                 NOT NULL    DEFAULT 0,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,

    CONSTRAINT PK_Feedback PRIMARY KEY (FeedbackID),
    CONSTRAINT FK_Feedback_Announcements FOREIGN KEY (AnnouncementID)
        REFERENCES Announcements(AnnouncementID),
    CONSTRAINT FK_Feedback_Users FOREIGN KEY (UserID)
        REFERENCES Users(UserID),
    CONSTRAINT CHK_Feedback_Rating CHECK (
        Rating BETWEEN 1 AND 5 OR Rating IS NULL
    ),
    CONSTRAINT CHK_Feedback_Sentiment CHECK (
        SentimentLabel IN ('Positive', 'Neutral', 'Negative')
        OR SentimentLabel IS NULL
    )
);
GO

-- ============================================================
--  TABLE 11: Events
--  Depends on: Announcements, Users
-- ============================================================
CREATE TABLE Events (
    EventID         INT IDENTITY(1,1)   NOT NULL,
    AnnouncementID  INT                 NULL,
    OrganizerID     INT                 NOT NULL,
    EventTitle      NVARCHAR(255)       NOT NULL,
    Location        NVARCHAR(255)       NULL,
    StartDateTime   DATETIME            NOT NULL,
    EndDateTime     DATETIME            NOT NULL,
    MaxAttendees    INT                 NULL,
    IsOnline        BIT                 NOT NULL    DEFAULT 0,
    MeetingURL      NVARCHAR(500)       NULL,
    CreatedAt       DATETIME            NOT NULL    DEFAULT GETDATE(),
    UpdatedAt       DATETIME            NULL,

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
--  TABLE 12: AIProcessingLogs
--  Depends on: Announcements
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
--  TABLE 13: ChatbotConversations
--  Depends on: Users
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
--  TABLE 14: RefreshTokens
--  Depends on: Users
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
--  TABLE 15: AuditLogs
--  Depends on: Users
-- ============================================================
CREATE TABLE AuditLogs (
    LogID           BIGINT IDENTITY(1,1) NOT NULL,
    UserID          INT                 NULL,
    Action          NVARCHAR(100)       NOT NULL,
    -- Action: CREATE, UPDATE, DELETE, LOGIN, LOGOUT
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
--  INDEXES
-- ============================================================
CREATE INDEX IX_Users_Email                 ON Users(Email);
CREATE INDEX IX_Users_RoleID                ON Users(RoleID);
CREATE INDEX IX_UserDepartments_UserID      ON UserDepartments(UserID);
CREATE INDEX IX_UserDepartments_TagID       ON UserDepartments(TagID);
CREATE INDEX IX_DepartmentTags_TagTypeID    ON DepartmentTags(TagTypeID);
CREATE INDEX IX_Announcements_Status        ON Announcements(Status);
CREATE INDEX IX_Announcements_FeedType      ON Announcements(FeedType);
CREATE INDEX IX_Announcements_AuthorID      ON Announcements(AuthorID);
CREATE INDEX IX_Announcements_UpdatedAt     ON Announcements(UpdatedAt);
CREATE INDEX IX_AnnouncementTags_TagID      ON AnnouncementTags(TagID);
CREATE INDEX IX_AnnouncementTags_AnnID      ON AnnouncementTags(AnnouncementID);
CREATE INDEX IX_Notifications_UserID        ON Notifications(UserID);
CREATE INDEX IX_Notifications_IsRead        ON Notifications(IsRead);
CREATE INDEX IX_Feedback_AnnouncementID     ON Feedback(AnnouncementID);
CREATE INDEX IX_AuditLogs_UserID            ON AuditLogs(UserID);
CREATE INDEX IX_AuditLogs_CreatedAt         ON AuditLogs(CreatedAt);
GO

-- ============================================================
--  SEED DATA: Roles
-- ============================================================
INSERT INTO Roles (RoleName, RoleLevel, Description, CanPublish, CanManageUsers)
VALUES
    ('Student',       1, 'Can view announcements and give feedback', 0, 0),
    ('Faculty',       2, 'Can publish academic announcements',       1, 0),
    ('Staff',         2, 'Can publish non-academic announcements',   1, 0),
    ('Administrator', 4, 'Full system access',                       1, 1);
GO

-- ============================================================
--  SEED DATA: TagTypes
-- ============================================================
INSERT INTO TagTypes (TypeName, Description)
VALUES
    ('Academic',    'College and department related announcements'),
    ('NonAcademic', 'Office and administrative announcements'),
    ('Emergency',   'Urgent campus wide alerts');
GO

-- ============================================================
--  SEED DATA: DepartmentTags — Academic Colleges
-- ============================================================
INSERT INTO DepartmentTags (TagName, ShortName, TagTypeID, Description, ColorHex)
VALUES
    ('School Wide',                                  'ALL',   1, 'Announcements for all students and staff',     '#1E40AF'),
    ('College of Engineering',                       'COE',   1, 'Engineering department announcements',         '#B45309'),
    ('College of Nursing',                           'CON',   1, 'Nursing department announcements',             '#0F766E'),
    ('College of Pharmacy',                          'COP',   1, 'Pharmacy department announcements',            '#6D28D9'),
    ('College of Architecture',                      'COA',   1, 'Architecture department announcements',        '#92400E'),
    ('College of Liberal Arts & Sciences',           'CLAS',  1, 'Liberal Arts and Sciences announcements',      '#0369A1'),
    ('College of Business Administration',           'CBA',   1, 'Business Administration announcements',        '#065F46'),
    ('College of Education',                         'COED',  1, 'Education department announcements',           '#78350F'),
    ('College of Law',                               'LAW',   1, 'Law department announcements',                 '#1E3A5F'),
    ('College of Computing & Information Technology','CCIT',  1, 'CCIT department announcements',                '#1D4ED8'),
    ('College of Science',                           'COS',   1, 'Science department announcements',             '#065F46'),
    ('Physical Education Department',                'PE',    1, 'PE department announcements',                  '#15803D'),
    ('Graduate School',                              'GS',    1, 'Graduate school announcements',                '#7E22CE');
GO

-- ============================================================
--  SEED DATA: DepartmentTags — Non-Academic Offices
-- ============================================================
INSERT INTO DepartmentTags (TagName, ShortName, TagTypeID, Description, ColorHex)
VALUES
    ('Accounting Office',   'ACCTG',   2, 'Payment deadlines, tuition fees, billing',     '#B45309'),
    ('Registrar Office',    'REG',     2, 'Enrollment schedules, grades, records',         '#0F766E'),
    ('Campus Store',        'STORE',   2, 'Sales, promos, merchandise',                    '#7C3AED'),
    ('Library',             'LIB',     2, 'Book availability, library hours, fines',       '#1E40AF'),
    ('Clinic',              'CLINIC',  2, 'Health advisories, medical services',           '#DC2626'),
    ('Student Affairs',     'OSA',     2, 'Student concerns, scholarships, activities',    '#0369A1'),
    ('Campus Security',     'SEC',     2, 'Safety announcements, lost and found',          '#374151');
GO

-- ============================================================
--  SEED DATA: DepartmentTags — Emergency
-- ============================================================
INSERT INTO DepartmentTags (TagName, ShortName, TagTypeID, Description, ColorHex)
VALUES
    ('Emergency', 'EMRG', 3, 'Urgent campus wide emergency alerts', '#DC2626');
GO

-- ============================================================
--  SEED DATA: AnnouncementCategories
-- ============================================================
INSERT INTO AnnouncementCategories (CategoryName, Description, ColorHex, IconName, IsEmergency)
VALUES
    ('Academic',        'Class schedules, exams, grades',        '#3B82F6', 'fa-book',        0),
    ('Extracurricular', 'Clubs, sports, campus events',          '#10B981', 'fa-star',        0),
    ('Administrative',  'School policies, office memos',         '#8B5CF6', 'fa-building',    0),
    ('Financial',       'Payments, tuition, billing',            '#F59E0B', 'fa-money-bill',  0),
    ('Health',          'Health advisories, clinic updates',     '#EF4444', 'fa-heart-pulse', 0),
    ('General',         'General campus information',            '#64748B', 'fa-info-circle', 0),
    ('Emergency',       'Urgent campus wide alerts',             '#DC2626', 'fa-exclamation', 1);
GO

-- ============================================================
--  SEED DATA: Default Admin User
--  IMPORTANT: Replace PasswordHash after ASP.NET setup
-- ============================================================
INSERT INTO Users (FirstName, LastName, Email, PasswordHash, RoleID, IsActive)
VALUES
    ('System', 'Administrator', 'admin@educonnect.edu',
     'REPLACE_WITH_BCRYPT_HASH', 4, 1);
GO

-- ============================================================
--  VERIFY: All tables created
-- ============================================================
SELECT
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_CATALOG = 'EduConnectDB'
ORDER BY TABLE_NAME;

-- ============================================================
--  VERIFY: Row counts of seeded tables
-- ============================================================
SELECT 'Roles'                   AS TableName, COUNT(*) AS Rows FROM Roles
UNION ALL
SELECT 'TagTypes',                              COUNT(*) FROM TagTypes
UNION ALL
SELECT 'DepartmentTags',                        COUNT(*) FROM DepartmentTags
UNION ALL
SELECT 'AnnouncementCategories',                COUNT(*) FROM AnnouncementCategories
UNION ALL
SELECT 'Users',                                 COUNT(*) FROM Users;

PRINT '============================================================';
PRINT ' EduConnectDB v4.0 — Fully Normalized';
PRINT ' Tables   : 15';
PRINT ' Indexes  : 16';
PRINT ' TagTypes : 3 (Academic, NonAcademic, Emergency)';
PRINT ' Tags     : 21 (13 Academic + 7 NonAcademic + 1 Emergency)';
PRINT ' Roles    : 4 (Student, Faculty, Staff, Administrator)';
PRINT ' Categories: 7';
PRINT '============================================================';