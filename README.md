# Online Concert Ticketing Reservation System

An ASP.NET Core MVC app for browsing concerts, picking seats on a live seat map, buying VIP/General tickets plus merchandise, paying through a local mock payment gateway, and managing everything from an admin back office.

## Tech stack

- ASP.NET Core MVC (.NET 9)
- Entity Framework Core 9 + SQL Server
- ASP.NET Core Identity (roles: `Admin`, `User`)
- SignalR (live seat status updates)
- Bootstrap 5 + vanilla JS (no frontend build step)

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A SQL Server instance reachable at `localhost:1433` — a local install (see below) or Docker (optional)
- The EF Core CLI tool: `dotnet tool install --global dotnet-ef`

## 1. Get a database running

The connection string in `appsettings.json` expects SQL Server on `localhost:1433` with user `sa` / password `root@123`. Use whichever of these fits your setup:

### Option A: Local SQL Server install (default expectation)

If you already have SQL Server running locally (Developer/Express edition on Windows, or an existing instance elsewhere), just make sure it matches what the app expects:

- **Mixed Mode Authentication** is enabled (so SQL logins like `sa` work, not just Windows auth) — set this during install, or change it later via SQL Server Configuration Manager / SSMS.
- The **`sa` login is enabled** with password `root@123` — or set your own password and update `ConnectionStrings:DefaultConnection` in `OnlineConcertTicketingReservationSystem/appsettings.json` to match.
- **TCP/IP is enabled on port 1433** (SQL Server Configuration Manager → SQL Server Network Configuration → Protocols) and the SQL Server (and SQL Server Browser) service is running.
- Don't have SQL Server yet? Grab the free [Developer edition](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (Windows). On macOS/Linux there's no native SQL Server install — use Option B (Docker) instead.

If your instance runs on a different host/port or uses different credentials, just edit `ConnectionStrings:DefaultConnection` in `appsettings.json` (or override it with a `Development`-scoped `appsettings.Development.json` / user-secrets so you don't touch the committed file).

### Option B: Docker (optional, cross-platform)

No local SQL Server install needed — spin up a disposable instance with the same credentials the app expects:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=root@123" \
  -p 1433:1433 --name concert-ticket-sqlserver \
  -d mcr.microsoft.com/azure-sql-edge:latest
```

(Any SQL Server 2019+ compatible image works — `mcr.microsoft.com/mssql/server` is fine too. Apple Silicon users should stick with `azure-sql-edge`, which has an arm64 image.)

## 2. Restore & build

```bash
cd OnlineConcertTicketingReservationSystem
dotnet restore
dotnet build
```

## 3. Run the app

```bash
dotnet run
```

On first run, `DbSeeder` (in `Data/DbSeeder.cs`) automatically:

- Applies all EF Core migrations (`db.Database.MigrateAsync()`), so you never need to run `dotnet ef database update` by hand for local dev
- Creates the `Admin` and `User` Identity roles
- Creates a seeded admin account
- Seeds 3 demo concerts, each with VIP/General ticket tiers, 72 seats (2 sections × 3 rows × 12 seats), and a handful of merchandise items

The app listens on `http://localhost:5082` (and `https://localhost:7175` if you run the `https` launch profile). It opens a browser automatically via `dotnet run`.

### Default admin login

| | |
|---|---|
| Email | `admin@concertticketing.local` |
| Password | `Admin_P@ssw0rd123` |

Override these before first run via `appsettings.json` / user-secrets / env vars:

```json
"AdminSeed": {
  "Email": "you@example.com",
  "Password": "Your_P@ssw0rd",
  "Name": "Your Name"
}
```

Once logged in as admin you'll see an **Admin** dropdown in the nav bar: Dashboard, Orders, Verification Queue, Validate Ticket, and Manage Catalog.

## 4. Resetting / re-seeding the database

Seeding only runs when the `Concerts` table is empty. To get a clean slate again (e.g. after testing bookings/cancellations):

```bash
dotnet ef database drop -f
dotnet run          # re-applies migrations and re-seeds on startup
```

## 5. Adding a new EF Core migration

After changing anything under `Models/` or `Data/ApplicationDbContext.cs`:

```bash
dotnet ef migrations add YourMigrationName
dotnet build         # sanity check it compiles
```

Migrations apply automatically the next time the app starts — no manual `database update` needed for local dev.

## Project layout

```
Controllers/   MVC controllers (Home, Account, Booking, Ticket, MockPayment, Admin, Management)
Models/        EF Core entities + Enums/ + ViewModels/
Data/          ApplicationDbContext, DbSeeder
Migrations/    EF Core migrations
Services/      SeatNotifier (SignalR), OrderExpirationWorker (background service)
Hubs/          SeatHub — live seat status over SignalR
Views/         Razor views, organized per-controller; Views/Shared has the layout/partials
wwwroot/       CSS, JS, vendored libs (Bootstrap/jQuery/SignalR client), uploaded payment slips
```

### Where things live, by feature

- **Booking flow**: `Controllers/BookingController.cs` → `Views/Booking/*` (seat selection, checkout, confirmation)
- **Live seat map**: `Hubs/SeatHub.cs` + `Services/SeatNotifier.cs` + `wwwroot/js/seat-hub.js`
- **Mock payment**: `Controllers/MockPaymentController.cs` — simulates a payment gateway, no real charge ever happens
- **Ticket QR codes / gate scanning**: `Controllers/TicketController.cs` (`MyTickets`, `Qr`, `Validate`)
- **Admin — orders, verification, cancellation, per-ticket revoke**: `Controllers/AdminController.cs` → `Views/Admin/*`
- **Admin — catalog (venues, artists, concerts, trailers, ticket tiers, seats, merchandise)**: `Controllers/ManagementController.cs` → `Views/Management/Index.cshtml`
- **Auth**: `Controllers/AccountController.cs` (ASP.NET Core Identity, email as username)

## Notable behaviors worth knowing before you dig in

- **Seat holds expire after 10 minutes.** `OrderExpirationWorker` is a background service that releases seats/expires orders whose payment lock has passed. See `ReserveSeats` in `BookingController` for where the 10-minute lock is set.
- **Mock payment is not real.** `MockPaymentController` just flips an order to Confirmed/Rejected — there's no external gateway integration. Payment "proof" for bank-transfer-style orders is an uploaded slip image, reviewed by an admin in the Verification Queue.
- **Ticket revocation has two levels**: `AdminController.CancelOrder` cancels a whole order (all tickets, full refund, merch restocked), while `AdminController.RevokeTicket` pulls a single ticket out of a multi-seat order and auto-cancels the order once every ticket in it is revoked.
- **VIP/General tiers** are `TicketType` rows per concert. Creating a ticket type doesn't assign it to seats by itself — use "Assign seat tier" in Management to apply it (and its price) to a whole seat section.
- **Merchandise photos and concert trailers** are stored as plain URLs (`Accessory.ImageUrl`, `Concert.TrailerUrl`), not uploaded files — point them at any image/video link, YouTube/Vimeo link, or direct `.mp4`. No URL means a placeholder is shown instead.
- **Payment slip uploads** are saved to `wwwroot/uploads/slips/` and validated for file type/size/magic bytes in `BookingController.ValidateSlipFileAsync`.

## Troubleshooting

- **"Failed to determine the https port for redirect"** in the console on startup — harmless in local dev; only relevant if you're not running the `https` launch profile.
- **Can't connect to SQL Server** — confirm the instance (local install or Docker container) is running and listening on `1433`, that Mixed Mode Authentication + the `sa` login are enabled if using a local install, and that the password in `appsettings.json` matches.
- **Migration/model mismatch errors** — run `dotnet ef database drop -f` followed by `dotnet run` to rebuild from scratch (see step 4).
