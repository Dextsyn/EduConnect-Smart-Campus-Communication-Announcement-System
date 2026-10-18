# EduConnect

EduConnect is a campus communication and announcement platform for Adamson University. Administrators, deans, chairpersons, faculty, staff and students use it to post and approve announcements, run events with QR check-in, manage student organizations, find study groups, file safety reports, and ask an AI assistant about what's been announced.

## Stack

- **ASP.NET Core 8 MVC** (Razor views), one web project: `src/EduConnect.Web`
- **Entity Framework Core 8** on **SQL Server** (SQL Server Express locally, Azure SQL in production)
- **SignalR** for live notifications, event updates and group chat
- **MailKit** over Gmail SMTP for account and event email
- **Google Gemini** (`Google.GenAI`) for the chatbot and announcement summaries
- **Azure Blob Storage** for every uploaded file and the event check-in QR codes (generated with QRCoder)
- **BCrypt** password hashing with session-based sign-in (no ASP.NET Identity)
- **xUnit** tests on in-memory SQLite: `src/EduConnect.Tests`
- Hosted on **Azure App Service**, deployed by GitHub Actions

## Repository layout

| Path | What's there |
| --- | --- |
| `src/EduConnect.Web` | The web app: controllers, views, services, SignalR hubs, EF migrations |
| `src/EduConnect.Tests` | Unit and controller tests |
| `src/EduConnect.Web.slnx` | Solution file |
| `database/` | One-off SQL scripts. `EduConnectDB.sql` is historical; the EF migrations are the schema's source of truth |
| `docs/superpowers/` | Design specs and implementation plans |
| `src/CLAUDE.md` | Detailed architecture notes (roles, approval routing, audiences, time zone rules) |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) (or any SQL Server instance) and, optionally, SQL Server Management Studio
- The EF Core CLI: `dotnet tool install --global dotnet-ef --version 8.*`
- Visual Studio 2022 17.13+ or Rider if you want to open the `.slnx` solution in an IDE; the `dotnet` commands below work without one

## Local setup

### 1. Clone and restore

```bash
git clone https://github.com/Dextsyn/EduConnect-Smart-Campus-Communication-Announcement-System.git
cd EduConnect-Smart-Campus-Communication-Announcement-System/src
dotnet restore EduConnect.Web
```

All commands below run from `src/`.

### 2. Database

The default connection string in `EduConnect.Web/appsettings.json` points at a local SQL Server Express instance with Windows authentication:

```
Server=localhost\SQLEXPRESS;Database=EduConnectDB;Trusted_Connection=True;TrustServerCertificate=True;
```

If your instance is named differently or uses SQL authentication, override `ConnectionStrings:EduConnectDatabase` in user secrets (next step) rather than editing the committed file. Then create the database and apply every migration:

```bash
dotnet ef database update --project EduConnect.Web
```

The migrations seed roles, announcement categories and the School Wide tag. They don't create any user, so add the first administrator by hand (see [First administrator](#first-administrator)).

### 3. Settings and secrets

`appsettings.json` is committed with every secret left blank. Supply real values with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (the project already has a `UserSecretsId`), or in an `appsettings.Development.json`, which `.gitignore` keeps out of the repo. Never commit a real value.

| Key | Needed for | Notes |
| --- | --- | --- |
| `ConnectionStrings:EduConnectDatabase` | Everything | Only if the default above doesn't match your SQL Server |
| `EmailSettings:SenderEmail` | Any email | The Gmail address mail is sent from |
| `EmailSettings:Password` | Any email | A Gmail [app password](https://support.google.com/accounts/answer/185833) for that address, not the account password |
| `EmailSettings:SmtpHost`, `SmtpPort`, `SenderName` | Email | Committed defaults: `smtp.gmail.com`, `587`, `EduConnect Admin` |
| `GeminiSettings:ApiKey` | Chatbot, AI summaries | A Google AI Studio API key |
| `GeminiSettings:Model` | Chatbot | Defaults to `gemini-2.5-flash` |
| `AzureBlobStorage` | Uploads and event QR codes | An Azure Storage connection string. See [Blob containers](#blob-containers) |
| `AppBaseUrl` | Links in emails and QR codes | Set it to `https://localhost:7135` locally. Left empty, the app uses the request's host |

```bash
dotnet user-secrets set "EmailSettings:SenderEmail" "<your-gmail-address>" --project EduConnect.Web
dotnet user-secrets set "EmailSettings:Password" "<gmail-app-password>" --project EduConnect.Web
dotnet user-secrets set "GeminiSettings:ApiKey" "<gemini-api-key>" --project EduConnect.Web
dotnet user-secrets set "AzureBlobStorage" "<storage-connection-string>" --project EduConnect.Web
dotnet user-secrets set "AppBaseUrl" "https://localhost:7135" --project EduConnect.Web
```

The app starts without these. Email failures are logged and the request carries on, the chatbot replies that it isn't configured, and anything that uploads a file (photos, attachments, event QR codes) fails until `AzureBlobStorage` is set. Without email you can't confirm a student registration or receive an Add User temporary password, so set the email keys before testing sign-up.

#### Blob containers

The app doesn't create containers, so create these in the storage account before uploading anything. The app links to blobs directly, so each needs blob-level (anonymous read) public access:

`announcements`, `events`, `qrcodes`, `profiles`, `org-logos`, `org-attachments`, `safety-reports`

For local work you can point `AzureBlobStorage` at a separate dev storage account, or at [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) with `UseDevelopmentStorage=true`.

### 4. First administrator

Generate a BCrypt hash for the password you want, outside the repo:

```bash
dotnet new console -o hashpw && cd hashpw
dotnet add package BCrypt.Net-Next
echo 'Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(args[0]));' > Program.cs
dotnet run -- "<password>"
```

Then run this against `EduConnectDB` with the printed hash:

```sql
INSERT INTO Users (FirstName, LastName, Email, PasswordHash, RoleID,
                   IsActive, VerificationStatus, MustChangePassword, CreatedAt)
SELECT 'System', 'Administrator', 'admin@example.edu', '<bcrypt-hash>', RoleID,
       1, 'Verified', 0, GETDATE()
FROM Roles WHERE RoleName = 'Administrator';
```

Sign in with that email and password. From the admin dashboard you can build the Colleges › Departments › Programs structure (Academic Structure) and add the other accounts (Manage Users › Add User, which emails each person a temporary password).

## Running

```bash
dotnet run --project EduConnect.Web --launch-profile https
```

The app listens on https://localhost:7135 (and http://localhost:5120) in the Development environment. Trust the dev certificate once with `dotnet dev-certs https --trust` if your browser warns.

Uploads are limited to 10 MB.

## Tests

```bash
dotnet test EduConnect.Tests
```

The tests use in-memory SQLite and fakes for email, blob storage and notifications, so they need no SQL Server, secrets or network.

## Database changes

Schema changes go through EF Core migrations:

```bash
dotnet ef migrations add <MigrationName> --project EduConnect.Web
dotnet ef database update --project EduConnect.Web
dotnet ef database update <PreviousMigrationName> --project EduConnect.Web   # roll back
```

Timestamps are stored in Manila time (the App Service runs on UTC+8). Raw SQL inside a migration runs on Azure SQL's UTC clock, so use `DATEADD(hour, 8, SYSUTCDATETIME())` there, not `SYSDATETIME()`. `src/CLAUDE.md` has the details and the one exception (Group Finder stores UTC).

## Deployment

Pushing to `main` runs `.github/workflows/main_educonnect.yml`, which builds and publishes `EduConnect.Web` in Release on a Windows runner and deploys it to the Azure App Service **EduConnect** (Production slot) using OpenID Connect. It can also be started by hand from the Actions tab. The workflow doesn't run the tests, so run `dotnet test EduConnect.Tests` before merging.

The App Service needs these application settings (Configuration › Application settings):

| Setting | Value |
| --- | --- |
| `ConnectionStrings__EduConnectDatabase` (or a connection string named `EduConnectDatabase`) | The Azure SQL connection string |
| `EmailSettings__SenderEmail`, `EmailSettings__Password` | Gmail sender and app password |
| `GeminiSettings__ApiKey` | Gemini API key |
| `AzureBlobStorage` | Storage connection string for the account holding the [blob containers](#blob-containers) |
| `AppBaseUrl` | The site's public URL, so emails and QR codes link to it |
| `WEBSITE_TIME_ZONE` | `Singapore Standard Time`, so `DateTime.Now` is Manila time |

The workflow doesn't apply migrations. After a deploy that adds one, apply it to Azure SQL yourself, for example from a machine allowed through the Azure SQL firewall:

```bash
dotnet ef database update --project EduConnect.Web --connection "<azure-sql-connection-string>"
```
