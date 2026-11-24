# Property Showing Booking API

A .NET 8 Web API for creating property showing slots and managing renter bookings.

## Features

- Create future showing slots with duration and capacity validation.
- List slots by property and date range with pagination and seats remaining.
- Book a slot for a renter party without exceeding capacity.
- Prevent duplicate active bookings by the same renter for a slot.
- Cancel bookings before the showing starts.
- Mark bookings as Attended or NoShow after the showing starts.
- View booking status history.
- Return appropriate 400, 404, and 409 responses.

## Technology

- ASP.NET Core Web API (.NET 8)
- Entity Framework Core 8
- SQLite
- xUnit integration tests

## API endpoints

| Method | Route | Purpose |
|---|---|---|
| POST | `/slots` | Create a showing slot |
| GET | `/slots` | List and filter showing slots |
| POST | `/slots/{id}/bookings` | Book a showing slot |
| POST | `/bookings/{id}/transitions` | Change booking status |
| GET | `/bookings/{id}/history` | Get booking history |

## Run locally

Install the .NET 8 SDK. From the solution root, run:

```bash
dotnet restore PropertyShowing.sln
dotnet run --project src/PropertyShowing.Api
```

The API listens on the URL shown in the terminal. SQLite database file `property-showings.db` is created automatically.

## Run tests

```bash
dotnet test PropertyShowing.sln
```

## Design notes

- Controllers handle HTTP requests; services contain booking rules; repositories handle data access.
- Booking capacity is reserved with an atomic conditional database update.
- A database unique index prevents more than one active booking for the same renter and slot.
- Booking changes, history records, and seat updates are saved transactionally.
- SQLite is configured with `EnsureCreated` for simple local setup.

## AI use

AI assistance was used to scaffold and review the implementation and tests. The code was reviewed against the assignment requirements.