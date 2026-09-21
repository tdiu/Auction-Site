# Appendix: `DisplayName` generation and the uniqueness race

**Scope:** generating a unique `DisplayName` on the OAuth signup path, and surviving the race that creates
**Companion doc:** [federated-login.md](federated-login.md) §4.1, which this expands

Companion to `federated-login.md` §4.1. Concerns only the OAuth signup path; the password
registration path is unchanged (the user supplies a name and gets a validation error on collision).

## The constraints, as they actually exist

Three unique indexes bear on the signup path:

| Index | Column | Unique? |
|---|---|---|
| `IX_AspNetUsers_DisplayName` | `DisplayName` | **yes** |
| `UserNameIndex` | `NormalizedUserName` | **yes** |
| `EmailIndex` | `NormalizedEmail` | **yes**, since `AddFederatedLoginSupport` |

Two of them cover the generated name, because `UserName` is set from `DisplayName`. A generated
name must satisfy both, and a name collision fires one of those two.

`EmailIndex` is the third, and it is the reason a duplicate email can also surface as a 23505 here.
That path is terminal, routed to the one-account-per-email refusal rather than into the retry loop,
for the reasons in §4.

## Why not check-then-insert

`UserManager.CreateAsync` already probes for a duplicate name via `UserValidator` before inserting,
returning a `DuplicateUserName` error. That probe is not a lock: two concurrent Google signups
deriving `alexsmith` can both pass it, and the loser then hits the unique index at `SaveChanges`.

So there are **two** collision signals to handle, and both mean the same thing:

- `IdentityResult` failure with code `DuplicateUserName` (validator caught it, the common case)
- `DbUpdateException` → `PostgresException` with SQLSTATE 23505 (the race)

Treating only the first is the bug. Treating the second as a 500 turns a rare race into a failed
login.

## 1. Deriving the base name

```csharp
// API/Services/DisplayNameGenerator.cs
using System.Globalization;
using System.Text;

namespace API.Services;

public static class DisplayNameGenerator
{
    private const int MaxBaseLength = 11; // leaves room for a 4-digit suffix within 15 chars
    private const int MinLength = 3;      // matches RegisterDto's minimum

    // Must exceed the last numbered arm in Candidate, so at least one attempt reaches the GUID
    // branch. Lower it and a signup can fail for want of a username.
    public const int MaxAttempts = 6;

    public static string Derive(string? providerName, string email)
    {
        var slug = Slugify(providerName);
        if (slug.Length < MinLength) slug = Slugify(email.Split('@')[0]);
        if (slug.Length < MinLength) slug = "user";

        return slug.Length > MaxBaseLength ? slug[..MaxBaseLength] : slug;
    }

    // Attempt 0 gets the clean name. Later attempts append random digits rather than probing
    // 2, 3, 4...: sequential probing degrades to O(n) once a base name is popular, and makes
    // concurrent signups collide repeatedly on the same next candidate.
    public static string Candidate(string baseName, int attempt) => attempt switch
    {
        0 => baseName,
        1 or 2 => baseName + Random.Shared.Next(100, 1000),
        3 or 4 => baseName + Random.Shared.Next(1000, 10000),
        _ => "user" + Guid.NewGuid().ToString("N")[..10]
    };

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var normalized = value.Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            // Drop the accents left behind by FormD rather than the accented letters themselves,
            // so "Renée" slugs to "renee" and not "ren".
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            // ASCII alphanumeric only.
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                slug.Append(char.ToLowerInvariant(c));
        }

        return slug.ToString();
    }
}
```

The last attempt is a guaranteed-unique GUID-derived name. It is deliberately unreachable in
practice; it exists so a signup can never fail for want of a username.

The character test is an explicit ASCII range rather than `char.IsLetterOrDigit`, which accepts
every Unicode letter. `FormD` folds Latin accents, so `Renée` reaches `renee`, but nothing folds
Cyrillic, Greek or CJK: `Дмитрий` would slug to `дмитрий`, seven characters, long enough that the
`< 3` guard never fires and the email fallback is never reached. Identity's default
`AllowedUserNameCharacters` is ASCII, so `CreateAsync` would return `InvalidUserName`, which is not
`DuplicateUserName` and therefore terminal. That user could not create an account at all, under a
name they never chose and cannot correct.

## 2. The insert loop

```csharp
var baseName = DisplayNameGenerator.Derive(request.Name, email);
var user = new AppUser { Email = email, EmailConfirmed = true, DisplayName = baseName };

await using var tx = await unitOfWork.BeginTransactionAsync(ct);

for (var attempt = 0; attempt < DisplayNameGenerator.MaxAttempts; attempt++)
{
    var candidate = DisplayNameGenerator.Candidate(baseName, attempt);
    user.DisplayName = candidate;
    user.UserName = candidate;

    await tx.CreateSavepointAsync("attempt", ct);

    IdentityResult result;
    try
    {
        result = await userManager.CreateAsync(user);
    }
    catch (DbUpdateException ex) when (IsNameCollision(ex))
    {
        await tx.RollbackToSavepointAsync("attempt", ct);
        continue;
    }
    catch (DbUpdateException ex) when (ex.IsUniqueViolation())
    {
        // EmailIndex. A concurrent signup for the same email lands here, and must not retry.
        return Result<AppUser>.Failure("Email already registered", FailureReason.Conflict);
    }

    if (result.Succeeded)
    {
        try
        {
            var link = await userManager.AddLoginAsync(user, new UserLoginInfo(
                request.Provider, request.ProviderKey, request.Provider));

            if (!link.Succeeded)
                return Result<AppUser>.Failure("Could not complete sign-in", FailureReason.InternalError);
        }
        catch (DbUpdateException ex)
        {
            // The transaction rolls back on dispose and takes the account row with it, so there is
            // no orphan to recover from; the user just retries.
            return Result<AppUser>.Failure("Could not complete sign-in", FailureReason.InternalError);
        }

        await tx.CommitAsync(ct);
        return Result<AppUser>.Success(user);
    }

    // Validator caught the duplicate before the insert, same meaning, retry.
    if (result.Errors.Any(e => e.Code == "DuplicateUserName"))
    {
        await tx.RollbackToSavepointAsync("attempt", ct);
        continue;
    }

    return Result<AppUser>.ValidationFailure(MapIdentityErrors(result.Errors));
}

// Unreachable in practice: the last attempt takes Candidate's GUID branch, which cannot collide.
// Reaching here means that branch stopped being reachable, not that we were unlucky.
return Result<AppUser>.Failure("Could not allocate a username", FailureReason.InternalError);
```

Trimmed for readability; the real loop in `AuthService.CreateExternalUserAsync` logs on each
terminal branch.

```csharp
private static bool IsNameCollision(DbUpdateException ex) =>
    ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "IX_AspNetUsers_DisplayName" or "UserNameIndex"
    };
```

`IsNameCollision` deliberately does **not** match `EmailIndex`, which is what makes the two catch
clauses mean different things. A name violation is retryable and rolls back to the savepoint; an
email violation is the one-account-per-email refusal and returns immediately. Ordering matters:
the narrow filter has to come first, because `IsUniqueViolation` would otherwise swallow name
collisions and turn a retryable condition into a refusal.

### Why the loop mutates one instance instead of building a fresh `AppUser` per attempt

This is the part that is easy to get wrong. `UserStore.CreateAsync` does `Context.Add(user)` then
`SaveChanges()`. When `SaveChanges` throws on the unique index, **the entity stays in the change
tracker in the `Added` state**. Constructing a new `AppUser` for the retry would leave the failed
one tracked, and the next `SaveChanges` would try to insert *both*, failing on the same
constraint forever.

Re-submitting the same instance sidesteps it: `Context.Add` on an already-`Added` entity is a
no-op, and `SaveChanges` emits the INSERT from the entity's current values, which now carry the
new name. `CreateAsync` re-runs `UpdateNormalizedUserNameAsync` internally, so the normalized name
stays in step.

The alternative is injecting `AppDbContext` into `AuthService` purely to call
`Entry(user).State = EntityState.Detached` on failure. That works too, but it puts EF plumbing in
a service that currently only knows `UserManager`, and `IUnitOfWork` doesn't expose the context.

Because this leans on `UserStore` internals, it needs the concurrency test in §3 to stay honest:
it is not self-evident from reading the loop.

### Why each attempt runs inside a savepoint

The create and the `AddLoginAsync` share one transaction, so a failure cannot strand an account
with no password and no provider link, whose email would then block both signup paths permanently.
That transaction fights this loop on Postgres: a unique violation **aborts the enclosing
transaction**, and every subsequent retry would fail with 25P02 instead of trying the next name.

Savepoints reconcile the two. Each attempt takes one before `CreateAsync` and rolls back to it on a
name collision, which leaves the entity `Added` in the change tracker exactly as the section above
requires. EF Core already creates an internal savepoint around `SaveChanges` when a transaction is
active, so the explicit ones are partly belt-and-braces, but the loop's correctness should not rest
on framework behaviour that is invisible from the loop.

### Non-issue worth noting

The validator's `FindByNameAsync` probe runs a SQL query while the failed entity is still tracked
locally. EF does not fold pending `Added` entities into query results, so the probe cannot collide
with the in-flight row and report a false duplicate against itself.

## 3. Where the behaviour is pinned

The race is tested for real rather than mocked, via `Testcontainers.PostgreSql`. An in-memory
provider cannot reproduce it, because it does not enforce the unique index the same way.

`DisplayNameGeneratorTests` covers `Derive` on a plain name, an accented name (`Renée` →
`reneeflemin`), a name of only symbols (falls back to the email local part), a non-Latin name
(same fallback), and an empty-everything case (falls back to `user`). It also asserts the property
underneath those cases: every candidate any attempt can produce is ASCII alphanumeric and within
the 15-character register limit, so a generated name can never be one Identity then refuses.

`ExternalLoginConcurrencyTests` covers what a single caller cannot reach:

- N concurrent signups deriving the same base name, asserting N accounts, all names distinct, no
  request failed. This is what exercises the 23505 path on the name indexes, since the validator
  absorbs collisions in the serial case.
- Two concurrent signups for the *same* email, which is the one case that hits the terminal 23505
  on `EmailIndex`. It proves the refusal routing and the transaction rollback in a single test:
  exactly one account survives, and the loser leaves no login row behind.
- The account row and its provider link landing together, which is the invariant the transaction
  exists to protect.

`AuthServiceTests` covers the serial cases: a suffix on a taken name with both users persisting,
and a Google signup whose email already belongs to a password account being refused without
creating anything.

## 4. Why the email rule needs a real index

Separate from the name race, but the same class of bug.

Email is the account identity, yet `RequireUniqueEmail` is only a validator probe. Two concurrent
signups for one email can both pass it and create two accounts, silently breaking the invariant the
whole linking policy rests on. `EmailIndex` was originally non-unique, so nothing below the
validator caught it; `AddFederatedLoginSupport` drops and recreates it as unique.

```csharp
modelBuilder.Entity<AppUser>()
    .HasIndex(u => u.NormalizedEmail)
    .IsUnique()
    .HasDatabaseName("EmailIndex");
```

Notes:

- Postgres permits multiple NULLs under a unique index, so users without an email are unaffected.
- The migration fails if duplicate emails already exist. The check before applying it:
  `SELECT "NormalizedEmail", count(*) FROM "AspNetUsers" GROUP BY 1 HAVING count(*) > 1;`
- With the index present, `DuplicateEmail` can surface as a 23505 on `EmailIndex`. That stays
  **terminal**, routed to the refusal, never into the retry loop.

This hardens existing behaviour rather than being a federation requirement; federation is what
makes the invariant load-bearing.

## Rejected alternatives

| Option | Why not |
|---|---|
| Always append a random suffix | Removes the race cheaply, but every user gets `alexsmith4821`. The clean-name-first ladder costs one extra round trip only on actual collision. |
| Sequential probing (`alexsmith2`, `alexsmith3`, …) | Prettier, but O(n) queries as a base name saturates, and concurrent signups converge on the same next candidate, which makes contention worse. |
| Postgres advisory lock around name allocation | Correct and simple to reason about, but serialises all signups on one lock for a collision that is already rare. The retry loop costs nothing on the common path; the lock costs something on every signup. |
| Let the login fail and show an error | Unacceptable for a first-time OAuth signup; the user has no way to act on it. |
