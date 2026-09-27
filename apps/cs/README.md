# apps/cs

.NET runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `server/` | The Server: Orleans silo and Edge API in one ASP.NET Core host | Epic 1, present as a silo with health endpoints |

Grains, the event journal, projectors and migrations arrive from Story 1.2 on.
Tests live in [`tests/cs`](../../tests/cs).
