# Property Showing Booking API

A small HTTP API where owners publish showing slots and renters book seats on them.
C# / .NET 8, ASP.NET Core minimal APIs, MediatR, FluentValidation, EF Core with **SQLite**.

## How to run

Requires the .NET 8 SDK.

```bash
dotnet restore
dotnet run --project src/ShowingBooking.Api      # http://localhost:5080
```

The SQLite file `showingbooking.db` is created in the working directory on first start.

## How to run the tests

```bash
dotnet test
```

- `tests/ShowingBooking.Tests.Unit`: domain rules, no database (state machine, seat math, time boundaries, duplicates, history).
- `tests/ShowingBooking.Tests.Integration`: the real app through `WebApplicationFactory`, one temporary SQLite file per test class and a controllable clock. Includes 400/404/409 cases and the parallel overbooking tests.

## Database: SQLite

Chosen because it is a real database with real transactions, needs no Docker, and lets me test concurrency honestly.
EF Core InMemory has no real transactions or write conflicts, so it cannot prove the "never overbook" requirement.
Tests use a temporary SQLite *file*, not `:memory:`, because parallel requests open separate connections.

## Endpoints

| Method | Path | Success | Notes |
|---|---|---|---|
| POST | `/slots` | 201 | `propertyId`, `startsAt` (future), `durationMinutes` 15-120, `capacity` 1-20 |
| GET | `/slots` | 200 | `propertyId`, `from` (inclusive), `to` (exclusive), `page` (default 1), `pageSize` (default 20, max 100). Ordered by start time, includes `seatsRemaining` |
| POST | `/slots/{id}/bookings` | 201 | `renterEmail`, `partySize` 1-6 |
| POST | `/bookings/{id}/transitions` | 200 | `{ "to", "reason", "actor": "owner" \| "renter" }` |
| GET | `/bookings/{id}/history` | 200 | Every state change, oldest first |

Errors are ProblemDetails with a stable `code` field: **400** `validation_failed`; **404** `slot_not_found`, `booking_not_found`;
**409** `not_enough_seats`, `duplicate_active_booking`, `slot_started`, `invalid_transition`, `actor_not_allowed`,
`too_late_to_cancel`, `too_early_to_complete`, `slot_busy`.

## Main design decisions

- **Layers (clean architecture):** Domain (no dependencies) ← Application (MediatR, validators, repository interfaces) ← Infrastructure (EF Core, SQLite, repository implementations) and Api (HTTP edge, wires everything together). Dependencies only point inward.
- **Persistence behind interfaces.** Application defines `ISlotRepository`, `IBookingRepository` and `IUnitOfWork`; Infrastructure implements them with EF Core. Application has no EF Core or SQLite reference, and an architecture test (`ArchitectureTests`) fails if that changes. Handlers never see `DbContext`, `DbSet` or `Include`.
- **Pragmatic exceptions:** entities have private parameterless constructors and navigation properties so EF Core can materialise them, and the `Version` concurrency field lives on the entities. The Domain still has no EF Core package reference.
- **Rules live in the entities.** `Slot.Book` does the seat math and duplicate check; `Booking.Transition` is the state machine. Both take `now` as a parameter (from an injected `TimeProvider`), so they are unit-tested without a database or real clock.
- **`Result<T>` for expected failures**, exceptions only for the unexpected. One place maps error kinds to 400/404/409.
- **Transition table**

  | From | To | Actor | When | Effect |
  |---|---|---|---|---|
  | Confirmed | Cancelled | renter or owner | `now < startsAt` | frees seats |
  | Confirmed | Attended / NoShow | owner | `now >= startsAt` | seats stay used |
  | Cancelled / Attended / NoShow | anything | nobody | final | 409 |

- **History:** the initial Confirmed state and every transition write a row. Entries are ordered by a database-generated id, not the timestamp, because two changes can share a timestamp.

### Concurrency (the critical requirement)

1. `Slot.BookedSeats` is stored on the slot, and `Slot.Version` is an EF Core **concurrency token**, incremented on every seat change. `Booking.Version` works the same way.
2. A booking loads the slot, runs the domain rule (seat check), and saves slot + booking + history row in **one** `SaveChanges`.
3. If another request changed the slot first, the UPDATE (`WHERE Version = @loaded`) touches 0 rows and EF throws `DbUpdateConcurrencyException`. It is translated to one application exception, the change tracker is cleared, and the **whole operation re-runs from fresh data** (up to 5 attempts, short random delay).
4. **Why it is correct:** a save can only succeed if nobody changed the slot since it was read, so the seat check always ran against the real current seat count. A retry either succeeds or fails the check with `not_enough_seats`. It cannot overbook.
5. SQLite busy/locked errors count as contention too. WAL mode is enabled and the driver's busy timeout applies.
6. A **partial unique index** on `(SlotId, RenterEmail) WHERE Status = 'Confirmed'` is a database-level backstop for the one-active-booking rule.
7. Cancelling goes through the same slot version, so a cancel racing a booking is safe, and two cancels of one booking free the seats once.

**Known trade-off:** under very heavy contention a request can use up its retries and get `409 slot_busy` although seats remain. I chose a spurious 409 over any risk of overbooking.

## Assumptions

1. No authentication: the `actor` in the body is trusted, and nobody checks that a renter owns the booking they cancel.
2. "Only before the slot starts" is strict: cancelling exactly at `startsAt` is rejected. Attended/NoShow at exactly `startsAt` is allowed.
3. "Active booking" means Confirmed. After cancelling, a renter can book the same slot again.
4. Seats used = Confirmed + Attended + NoShow. Cancelled frees seats.
5. Booking a slot that has already started (`now >= startsAt`) is a 409 `slot_started`.
6. `to: "Banana"` or a number is a 400. `to: "Confirmed"` is a valid name but never a legal target, so 409.
7. All times are UTC. Emails are trimmed and lowercased before the duplicate check.
8. `reason` is optional, max 500 characters.
9. Overlapping slots for the same property are allowed.
10. `GET /slots`: `from` inclusive, `to` exclusive, `from >= to` is a 400.

## What I skipped / would do with more time

- Real authentication and owner/renter identity (a slot would belong to an owner, a booking to a renter).
- EF Core migrations instead of `EnsureCreated`.
- Retry tuning or a per-slot queue to avoid `slot_busy` under extreme contention, plus a load test.
- Moving the Version check into a `UPDATE ... WHERE BookedSeats + @n <= Capacity` statement as a second line of defence.
- Separate persistence classes (mapping layer) so Domain entities carry no EF-driven shape at all.
- Structured logging, health checks, Dockerfile, OpenAPI/Swagger.
- Overlap rules for slots on one property, and a way for an owner to cancel a whole slot.

## AI use

I used an AI assistant (Claude) throughout, as the brief allows. I wrote the working prompt first, with every business rule and edge case, and told it to follow a fixed process: list ambiguities and assumptions, propose the design, wait for my approval, then build in small slices.

**What I used it for**
- Turning the brief into a list of ambiguities and proposed assumptions (strict "before the slot starts", what "active booking" means, which states use seats, error codes).
- Proposing the architecture, entity model, transition table and the concurrency approach.
- Generating the first version of the domain, handlers, endpoints, unit tests, integration tests and this README.
- Organising the git history into one commit per development phase.

**What I changed or rejected**
- **Architecture.** The first version let the Application layer use EF Core directly (handlers saw `DbSet` and `Include`). I asked for stricter clean architecture, so persistence now sits behind `ISlotRepository`, `IBookingRepository` and `IUnitOfWork`, the EF Core package was removed from the Application project, and an architecture test guards the dependency rule.
- **Git history.** The first history was a few large commits. I asked for it to be rebuilt as phase-wise commits (domain, unit tests, application, infrastructure, API, integration tests, concurrency tests, architecture tests, docs).
- **Target framework.** I kept the project on .NET 8 because the brief asks for it, even though my usual stack is newer.
- **Assumptions.** I reviewed the assumptions table before approving the design, and I can defend each one in the interview.

**What it got wrong**
- The assistant had no .NET SDK in its workspace, so it could not compile or run anything. None of the generated code or tests was verified when it was written. The first real build and test run was on my machine.
- Its first design leaked EF Core into the Application layer (see above).
- Its first commit history did not show the phases of development (see above).
- _Build errors and failing tests I found on my first `dotnet test` run, and how I fixed them: add here after running the tests._
