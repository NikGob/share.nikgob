# GDrive API

Self-hosted file upload API that stores files on Google Drive and generates short links via [Shlink](https://shlink.io). Features role-based access control, collection management, and a Swagger UI.

## Features

- **File Upload** — Upload files to Google Drive, get a short link back
- **Short Links** — Auto-generated via Shlink with custom or random slugs
- **Collections** — Tag files into named collections, browse by collection
- **Role-Based Auth** — Admin and user roles with token-based authentication
- **Ownership** — Users can only delete/update their own files; admins can manage all
- **Swagger UI** — Interactive API docs with request duration display

## Tech Stack

| Component | Technology |
|---|---|
| Framework | ASP.NET Core 10 |
| Database | MongoDB |
| File Storage | Google Drive API (OAuth2) |
| Short Links | Shlink |
| Docs | Swagger / Swashbuckle |

## Security

### Google Drive Scope — `drive.file`

This application uses the **`drive.file`** OAuth2 scope, which means:

> **The app can ONLY access files it created.** Even if the refresh token is compromised, an attacker cannot read, modify, or delete any files on your Google Drive that were not uploaded through this application.

This is the most restrictive Google Drive scope suitable for file uploads. See [Google Drive API scopes](https://developers.google.com/drive/api/guides/api-specific-auth).

### Authentication

- All endpoints require a **Bearer token** in the `Authorization` header
- On first startup (no tokens in DB), an **admin** token is auto-generated and printed to the console
- Admins create additional admin or user accounts via `POST /api/v1/accounts`
- Tokens are 64-character hex strings (two GUIDs)

### Secrets

- All secrets live in `appsettings.json` which is **gitignored**
- `appsettings.Example.json` is provided as a template

## Setup

### 1. Clone and configure

```bash
git clone https://github.com/YOUR_USERNAME/GDriveApi.git
cd GDriveApi/GDriveApi
cp appsettings.Example.json appsettings.json
```

Edit `appsettings.json` with your credentials:

| Key | Description |
|---|---|
| `MongoDb:ConnectionString` | MongoDB connection string |
| `MongoDb:DatabaseName` | Database name |
| `Google:FolderId` | Google Drive folder ID for uploads |
| `Google:ClientId` | OAuth2 Client ID |
| `Google:ClientSecret` | OAuth2 Client Secret |
| `Google:RefreshToken` | OAuth2 Refresh Token |
| `Shlink:BaseUrl` | Shlink instance URL |
| `Shlink:ApiKey` | Shlink API key |
| `Shlink:Domain` | Short link domain |

### 2. Get Google OAuth2 Refresh Token

1. Create a project in [Google Cloud Console](https://console.cloud.google.com/)
2. Enable the **Google Drive API**
3. Create OAuth2 credentials (Web application), set redirect URI to `http://localhost`
4. Visit the authorization URL:
   ```
   https://accounts.google.com/o/oauth2/v2/auth?client_id=YOUR_CLIENT_ID&redirect_uri=http://localhost&response_type=code&scope=https://www.googleapis.com/auth/drive.file&access_type=offline&prompt=consent
   ```
5. Copy the `code` parameter from the redirect URL
6. Exchange for a refresh token:
   ```bash
   curl -X POST https://oauth2.googleapis.com/token \
     -d "code=YOUR_CODE&client_id=YOUR_CLIENT_ID&client_secret=YOUR_CLIENT_SECRET&redirect_uri=http://localhost&grant_type=authorization_code"
   ```
7. Copy `refresh_token` into `appsettings.json`

### 3. Run

```bash
dotnet run
```

The admin token will be printed to the console on first run.

## Docker

### Docker Compose (recommended)

1. Copy the example file and fill in your values:
   ```bash
   cp docker-compose.example.yml docker-compose.yml
   ```
2. Edit `docker-compose.yml` — set all environment variables (MongoDB, Google, Shlink).
3. Start:
   ```bash
   docker compose up -d
   ```

All configuration is passed through environment variables — no need to mount `appsettings.json`.

### Standalone Docker

```bash
docker build -t gdrive-api .
docker run -p 8080:8080 \
  -e MongoDb__ConnectionString=mongodb://user:password@host:27017/ \
  -e MongoDb__DatabaseName=gdrive-api \
  -e Google__FolderId=FOLDER_ID \
  -e Google__ClientId=CLIENT_ID \
  -e Google__ClientSecret=CLIENT_SECRET \
  -e Google__RefreshToken=REFRESH_TOKEN \
  -e Google__BaseDownloadUrl=https://drive.usercontent.google.com/download?id= \
  -e Google__ImageDownloadUrl=https://lh3.googleusercontent.com/d/ \
  -e Shlink__BaseUrl=https://your-shlink-instance.com \
  -e Shlink__ApiKey=API_KEY \
  -e Shlink__Domain=your-short-domain.com \
  gdrive-api
```

## API Reference

### Files (`/api/v1/files`)

| Method | Endpoint | Description | Auth |
|---|---|---|---|
| `POST` | `/upload` | Upload a file (multipart/form-data) | Any |
| `GET` | `/{slug}` | Get file info by slug or short URL | Any |
| `DELETE` | `/{slug}` | Delete a file by slug or short URL | Owner or Admin |
| `PATCH` | `/{slug}` | Update file metadata by slug or short URL | Owner or Admin |

### Collections (`/api/v1/collections`)

| Method | Endpoint | Description | Auth |
|---|---|---|---|
| `GET` | `/` | List all collection names | Any |
| `GET` | `/{name}` | List files in a collection | Any |

### Accounts (`/api/v1/accounts`)

| Method | Endpoint | Description | Auth |
|---|---|---|---|
| `POST` | `/` | Create a new account | Admin |
| `GET` | `/` | List all accounts | Admin |
| `DELETE` | `/{id}` | Deactivate an account | Admin |

### Upload Request Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `file` | File | Yes | — | The file to upload |
| `slug` | string | No | random | Custom slug for the short link |
| `slugLength` | int | No | 8 | Length of random slug (3–64) |
| `collection` | string | No | `"default"` | Collection tag |
| `title` | string | No | null | Title for the short link |
| `crawlable` | bool | No | null | Whether the short link is crawlable (Shlink default if omitted) |
| `customFileName` | string | No | null | Custom display name for the file on Google Drive |
| `renameFile` | bool | No | false | Use `customFileName` instead of the original filename |

## License

This project is licensed under [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/).
