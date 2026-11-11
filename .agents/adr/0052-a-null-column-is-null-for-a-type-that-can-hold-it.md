# 0052 — A NULL column is null for a type that can hold it

- **Status:** Accepted
- **Date:** 2026-11-11
- **Builds on:** [0015](./0015-the-linked-index-is-loosely-coupled.md) (the linked index and its payload columns),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze begins with the tag)
- **Affects:** `LuiPayloadRow.Get<T>`, `docs/api.md`, the specification §12.3 and §12.8, `CHANGELOG.md`
- **Issue:** #111, found in #104 (pull request #109). The owner's decision of 2026-11-09: change it before 0.1.0.

## Context

`LuiPayloadRow.Get<T>` handed the column to `SqliteDataReader.GetFieldValue<T>`. For a `NULL`, Microsoft.Data.Sqlite
gives:

| `T` | before |
|---|---|
| `long?`, `DateTime?`, `string` | `InvalidOperationException` ("The data is NULL at ordinal …") |
| `object` | `DBNull.Value` |
| `long`, `DateTime` | `InvalidOperationException` |

So the line a reader of a mapping expects to work, `row.Get<long?>("discount")`, threw on the first row without a
value. A mapping of an optional column had to write `row.IsNull("discount") ? null : row.GetInt64("discount")`.
Pull request #109 wrote that rule down and pinned it in a test.

## Decision

**`Get<T>` returns `default` for a `NULL` when `T` can hold null, and throws when it cannot.**

| `T` | after |
|---|---|
| a nullable value type (`long?`, `DateTime?`) | `null` |
| a reference type (`string`, `byte[]`, `object`) | `null` |
| a value type (`long`, `DateTime`) | `InvalidOperationException`, as before |

- The test is `default(T) is null`, which holds exactly for a reference type and a `Nullable<>`. Only then is the column
  asked `IsDBNull`. Every other value goes to Microsoft.Data.Sqlite unchanged.
- **A `NULL` read as a `long` still throws.** A missing value read as `0` would be a wrong value without a word: the
  failure that the review of 2026-11-04 (A.7) guarded against for a kept row.
- **`GetString`, `GetInt64` and `GetDouble` stay as they are.** They name a type that cannot be null, and `IsNull` is
  the question for them.

Before 0.1.0 this is allowed: the freeze begins with the tag (ADR-0026). It changes the behaviour of a public method,
so it has this ADR and an entry under *Changed* in `CHANGELOG.md`.

## What was measured

- **Red first.** The test of #109 that pinned the refusal was changed to the new rule. Together with a new test
  (`LinkedQueryTests.A_null_column_is_null_for_a_type_that_can_hold_null_and_refused_for_one_that_cannot`: `long?`,
  `string`, `DateTime?` read as `null`, `long` refused), it was red against the old `Get<T>` and green against the new one.
- **The behaviour of Microsoft.Data.Sqlite for a `NULL`,** probed directly on `SELECT NULL`: `GetFieldValue<object>`
  gives `DBNull.Value`, and `GetFieldValue<string>` throws `InvalidOperationException`.
- **The guard bites.** The mutation `a-null-read-as-null-for-every-type-refused` (the `default(T) is null` check
  switched off) is caught.

## Consequences

- A mapping reads an optional column in one call: `row.Get<long?>("discount")`, `row.Get<string>("note")`.
- **`Get<object>` on a `NULL` gives `null`, not `DBNull.Value`.** A mapping that compared with `DBNull.Value` must
  compare with `null`. Before 0.1.0 no such caller exists outside this repository, and none inside it.
- One more call per read of a type that can hold null (`IsDBNull`, a check of the type of the column in SQLite). A
  payload is read for the hits only, at most `MaxResults` rows.

## Alternatives

- **Keep the rule and document it** (the state of #109). Rejected by the owner: the natural line throws, and the
  workaround is the line every mapping has to know.
- **`default` for every `T`, `0` for a `long`.** Rejected: a missing value would become a value.
- **A `GetNullable<T>` beside `Get<T>`.** Rejected: one more method to choose between, where the type the caller writes
  already says whether null is a value.
