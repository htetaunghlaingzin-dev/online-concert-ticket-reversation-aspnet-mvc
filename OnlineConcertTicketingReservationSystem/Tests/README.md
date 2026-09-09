# Ticket stock verification

Run on Windows with SQL Server LocalDB installed:

```powershell
dotnet run --project Tests/TicketStock.Integration/TicketStock.Integration.csproj --configuration Verification
```

The suite creates a uniquely named `ConcertStockTest_*` database, migrates an older seat-based schema containing sample records, and runs the real SQL Server provider and controllers. It deletes its own database afterward. It does not read or modify the application's configured database. `-- --keep` retains the generated database for local UI inspection.

Coverage includes migration preservation, ticket creation and editing, stale stock edits, invalid quantities, competing customers, duplicate callbacks, saved prices, payment failure and retry, manual payment approval, slip upload, insufficient stock during approval, transaction rollback, QR validation, and legacy booking cancellation/expiration. No additional test framework packages are required.

## Application upgrade

The application applies the included `AddTicketStockBooking` migration during startup through the existing seeder. Stop older running application instances before starting the updated version so legacy seat writers cannot continue changing inventory during the transition.

Existing numeric prices and totals are unchanged. For existing ticket types with seats, available stock starts from unreserved, available seats. Held and booked seats remain outside that stock until their older order releases them. Types without seats retain their non-negative configured capacity as available stock. A single unambiguous General/Normal type becomes GA; other historical types and unassigned seats are retained for record preservation and require admin review before offering new inventory. Missing VIP, GA, or VVIP types start with zero stock and must be configured by an admin.

Pending ticket orders do not hold inventory or display a countdown. Final payment or manual approval rechecks availability and commits the payment, deduction, and QR ticket issuance together. SQL Server transaction-owned application locks coordinate payment, approval, cancellations, and stock editing across app instances; conditional stock updates also prevent negative inventory. Historical order and ticket prices are saved independently from current ticket type prices.

The existing gateway is a development mock and does not charge real money. Payment-slip submission has been removed from checkout; existing submitted slips remain available for admin verification. No separate payment-method catalog is needed.

The migration refuses rollback after new ticket orders exist to prevent data loss. Apply subsequent changes with forward migrations.

Admin Orders displays all statuses in one list. Status updates confirm pending orders using the same inventory transaction, reject or cancel pending orders without adding stock, and cancel confirmed orders with a single stock restoration. Closed orders cannot be reopened, and stale status edits are rejected.
