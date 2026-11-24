# Property Showing Booking API

A .NET 8 / ASP.NET Core HTTP API for publishing property showing slots and booking renter parties.

## Tech stack

- .NET 8 and ASP.NET Core Web API
- Entity Framework Core 8
- SQLite (`property-showings.db`)
- xUnit and ASP.NET Core integration testing
- GitHub Actions CI

## Architecture and design decisions

The solution uses a small Clean Architecture structure with explicit dependency direction:

- **PropertyShowing.Domain** contains the core booking entities and status enum. It has no project dependencies.
- **PropertyShowing.Application** contains request/response models, repository contracts, and booking use-case logic. It depends only on Domain.
- **PropertyShowing.Infrastructure** implements repository contracts and contains EF Core / SQLite persistence. It depends on Application (and transitively Domain).
- **PropertyShowing.Api** contains HTTP controllers, startup, and dependency-injection composition. It references Application and Infrastructure.
- **PropertyShowing.Tests** contains API integration tests.

The dependency direction keeps business rules independent of EF Core and HTTP concerns. Infrastructure translates database uniqueness failures into an application-owned exception so the application layer does not need an EF Core dependency.

### Concurrency and capacity

A slot stores `BookedSeats`. Booking uses a single conditional SQL `UPDATE` (`BookedSeats + partySize <= Capacity`) to reserve seats. The update is atomic in SQLite, so concurrent requests cannot both allocate the same remaining seats. A filtered unique index on `(SlotId, RenterEmail)` for `Status = Confirmed` protects the one-active-booking rule at the database level. Seat allocation and booking/history insertion run inside one database transaction, so a failed insert rolls back the seat update as well.

Cancellation updates the booking status, writes its audit-history row, and releases the party's seats inside one transaction. `Attended` and `NoShow` do not release seats because those renters occupied their reserved places.

### Persistence

SQLite is used. On startup the API calls `EnsureCreatedAsync()` to create the local database, keeping setup simple for the assignment. For production, replace this with reviewed EF Core migrations and a managed database.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/slots` | Create a future showing slot |
| `GET` | `/slots?propertyId=&from=&to=&page=&pageSize=` | Filtered, start-time-ordered paginated slots including seats remaining |
| `POST` | `/slots/{id}/bookings` | Confirm a renter party booking |
| `POST` | `/bookings/{id}/transitions` | Transition a booking state |
| `GET` | `/bookings/{id}/history` | Read the booking's state-change history |

Expected errors use `400` for invalid input, `404` for missing slots/bookings, and `409` for invalid transitions, duplicate active bookings, or insufficient capacity.

## Run locally

Requires the .NET 8 SDK.

```bash
dotnet restore PropertyShowing.sln
dotnet run --project src/PropertyShowing.Api
```

The API creates `property-showings.db` in its content directory. The default connection string is in `src/PropertyShowing.Api/appsettings.json`; override `ConnectionStrings__BookingDatabase` through the environment for another location.

Swagger/OpenAPI UI is not currently configured in this project. Use the endpoint list and example requests below, or an HTTP client such as curl or Postman, to call the API.

## Run tests

```bash
dotnet test PropertyShowing.sln
```

The integration test suite currently contains 25 test cases. It covers slot validation and listing, booking capacity and duplicate-booking rules, concurrent last-seat reservations, booking transitions and permissions, history retrieval, error responses, and pagination boundaries. GitHub Actions restores, builds, and tests the solution on pushes and pull requests.

## Example requests

Create a slot:

```http
POST /slots
Content-Type: application/json

{
  "propertyId": "c3a8ca1d-9446-4c87-b419-16668c14eab1",
  "startsAt": "2030-01-15T10:00:00Z",
  "durationMinutes": 30,
  "capacity": 6
}
```

Book a party:

```http
POST /slots/{slotId}/bookings
Content-Type: application/json

{
  "renterEmail": "renter@example.com",
  "partySize": 2
}
```

Cancel before the showing starts:

```http
POST /bookings/{bookingId}/transitions
Content-Type: application/json

{
  "to": "Cancelled",
  "reason": "Schedule changed",
  "actor": "renter"
}
```

Mark attended after the showing has started (owner only):

```http
POST /bookings/{bookingId}/transitions
Content-Type: application/json

{
  "to": "Attended",
  "reason": "Renter arrived",
  "actor": "owner"
}
```

## Main limitations / what I would improve with more time

1. Add EF Core migrations and a production database configuration.
2. Add authentication and authorization. The assignment models owner/renter actor intent in the request, but a production system must derive the actor and ownership from authenticated claims, never trust the request body.
3. Add authentication and authorization tests once real owner and renter identities are introduced.
4. Consider running the concurrency test against file-backed SQLite and under repeated load in CI, in addition to the current integration test.
5. Add structured logging, health checks, OpenAPI response schemas, and API versioning if required by the host environment.
6. Move slot filtering, ordering, and pagination back into database-side queries if the persistence provider supports the required `DateTimeOffset` operations, or store timestamps in a provider-friendly normalized representation. The current SQLite implementation materializes the slots for a property before applying those operations in memory.

## AI use

Claude was used as a development assistant to review the assignment requirements, suggest the initial layered project structure, help draft implementation and integration-test changes, and diagnose failures from local build/test output. The developer reviewed the suggestions, applied the changes locally, and ran the test suite.

Examples of AI assistance and review outcomes:

- AI suggestions were used for separating slot and booking service responsibilities, repository contracts, validation, and integration-test scenarios.
- The initial diagnosis of the booking-history concurrency failure was incomplete. The EF Core log showed a `BookingHistory` entry being updated when it needed to be inserted. The persistence path was changed to explicitly add new history rows through the repository, and the local test suite was rerun.
- SQLite provider limitations surfaced when ordering `DateTimeOffset` values in queries for booking history and slot listing. Those query operations were adjusted to run after materialization in .NET. This is acceptable for the small assignment dataset, but it is a scalability limitation to revisit for larger datasets.
- An earlier README statement said Swagger was enabled in Development. Code review showed that Swagger was not configured, so that statement was removed and the limitation is now stated accurately.
- AI-generated changes were not treated as verified merely because they were suggested. The developer ran `dotnet test PropertyShowing.sln` locally and confirmed 25 test cases passed with no failures.
