# packages/cs

.NET libraries the Server references.

| Folder | What |
| --- | --- |
| `contracts/` | `Coldframe.Contracts`: event contracts, `EventTypeAttribute` and `IEventUpcaster<TFrom, TTo>` |

Tests live in [`tests/cs`](../../tests/cs).

## Event contracts

Every journaled event is a public record in `contracts/` with a stable alias and a schema version:

```csharp
[EventType("lot.renamed")]
public sealed record LotRenamed(string Name);
```

- The alias is `<entity>.<verb-ed>` in lowercase, past tense (`site.created`, `lot.removed`). It is
  written to every journal row and never changes; the record may be renamed or moved freely.
- The schema version starts at 1. A breaking payload change adds a new record with the next version and
  an `IEventUpcaster<TOld, TNew>`; the old record stays. See
  [`apps/cs/README.md`](../../apps/cs/README.md#add-an-upcaster).
- Payloads are System.Text.Json, camelCase, enums as strings, absent optional fields omitted.

Nothing here reads the clock: `DateTime.UtcNow` and its relatives fail the build.
