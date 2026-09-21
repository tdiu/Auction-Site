# Federated (OAuth) login

**Scope:** signing in with a third-party account (Google) instead of email + password
**Companion doc:** [federated-login-appendix-displayname.md](federated-login-appendix-displayname.md)
for the name-generation race

Google sign-in reuses the existing token-issuing pipeline essentially untouched. Two rules cut most
of the scope: no account linking, and no completion page. What remains is a `DisplayName`
generation strategy that has to survive a uniqueness race, and the refusal paths that enforce
one account per email. The OAuth plumbing itself is the easy part.

---

## 1. What it is built on

| Piece | Where | Relevance |
|---|---|---|
| ASP.NET Identity Core + EF stores | `API/Program.cs`, `AddIdentityCore<AppUser>` | Brings the full Identity schema |
| `AspNetUserLogins` table | Identity schema, `AddFederatedLoginSupport` migration | The standard provider-to-user link table |
| Single token-issue choke point | `AuthService.IssueAuthTokenAsync` | External login calls it directly and gets identical session semantics |
| Opaque refresh token in HttpOnly cookie | `AccountController.SetRefreshTokenCookie` | Lets the OAuth redirect complete **without putting any token in a URL**, see §3 |
| Client boots by calling `refresh-token` | `client/src/core/services/init-service.ts` | The SPA already knows how to pick up a session from a cookie alone |

No `SignInManager` is registered, and none is needed: `HttpContext.AuthenticateAsync` reads the
same ticket that `GetExternalLoginInfoAsync` would, so adding `.AddSignInManager()` would only
introduce a second sign-in surface.

---

## 2. Why the server-side flow

The server-side authorization-code flow (`AddGoogle`) is the standard ASP.NET remote-auth handler:
the browser navigates to the API, the API challenges Google, Google redirects back, the API mints
its own JWT and refresh cookie, then redirects to the SPA.

It wins on one property: adding Apple, Facebook or Microsoft later is one `AddX()` call plus
config. The provider token never touches JavaScript, and the client's token handling needs no
changes at all. The cost is a cookie scheme registered purely to carry the intermediate identity,
and `GoogleOptions.SignInScheme` has to be set explicitly, because the default falls through to
JWT bearer, which cannot sign in and fails at request time rather than at startup.

The alternative, rendering Google's button client-side and POSTing the ID token, is materially
less code and gives One Tap for free, but it does not generalize: every additional provider means
another JS SDK and another bespoke token-validation path, and validating `aud` / `iss` / `exp` /
`nonce` and JWKS rotation becomes ours to get right. It would be the better call if Google were
permanently the whole story.

**Staying multi-provider-ready without over-building.** Two habits are enough. The provider is
keyed off `AspNetUserLogins` (`LoginProvider` + `ProviderKey`) rather than a Google-specific column
on `AppUser`, and the challenge endpoint takes the scheme as a route value,
`/api/account/external-login/{provider}`, validated against a small allow-list. Adding a provider
means registering the scheme and extending the list. Name generation and the refusal rule are
provider-agnostic already.

---

## 3. The flow

Because the refresh token is already an HttpOnly cookie and the SPA already calls `refresh-token`
on boot, the callback never has to hand a token to the client through a URL fragment.

1. SPA does a **top-level navigation** (not XHR, since the challenge must be a real navigation) to
   `${apiUrl}/account/external-login/google?returnUrl=/orders`.
2. The challenge endpoint returns `Challenge(props, "Google")`.
3. Google redirects to the pinned callback path; the framework handler exchanges the code and signs
   the external identity into the temporary cookie scheme.
4. The callback endpoint reads that identity, resolves or creates the `AppUser` (§4), calls
   `IssueAuthTokenAsync`, and sets the refresh cookie via the existing helper.
5. `302` to the SPA.
6. SPA boots, `InitService` calls `refresh-token`, the cookie is present, the user is signed in.

`CallbackPath` is pinned to `/api/signin-google` rather than the framework default
`/signin-google`, so a reverse proxy forwarding only `/api/*` cannot 404 it.

**`returnUrl` is validated server-side.** The client's own guard in `login.ts` does not apply once
the server is the redirector, so `?returnUrl=//evil.com` would otherwise produce an open redirect
issued by our own domain. It is validated on the way out by `ReturnUrlPolicy.Safe`, carried inside
`AuthenticationProperties.Items` (the Google handler serialises those into the OAuth `state`
parameter under data protection, so it is signed and encrypted in transit), re-validated on the way
back, and the final URL is always built by prefixing an absolute `ClientAppUrl`, so the authority is
fixed before the untrusted segment is appended.

### Cookie and CORS notes

- Prod is same-origin (`apiUrl: 'api'`), so the refresh cookie's `SameSite=Strict` is fine.
- Dev is `https://localhost:4200` → `https://localhost:5001`. Different origins but the **same
  site** (port is not part of "site", and both are https), so `Strict` holds through the OAuth
  redirect. The one genuinely cross-site hop, Google to the callback, only *sets* the cookie, and
  setting is permitted.
- The **external** cookie is a different matter: it is set on the Google hop and read on the
  redirect back, so it is `Lax`. Copying `SetRefreshTokenCookie`'s `Strict` would break the flow
  with a symptom that reads as "Google login just doesn't work."
- The framework's correlation cookie is `SameSite=None; Secure`, framework-managed.
- Google requires an HTTPS redirect URI, localhost exempted. Dev HTTPS is set up already.

---

## 4. Account rules

**No completion page.** The first Google sign-in creates the account immediately, with a generated
`DisplayName` and no date of birth. An earlier draft proposed a "choose your username"
interstitial; that over-weighted the `DisplayName` constraint. Avoiding it is not free, though: it
converts one UX step into the two schema consequences below.

**4.1 `DisplayName` is generated under real uniqueness pressure.** Uniqueness is a hard database
constraint: a unique index on `DisplayName`, plus Identity's own `NormalizedUserName` index, since
`UserName` is set from `DisplayName`. The strategy is to slugify Google's `name` claim, fall back to
the email local part when that yields nothing usable, and append a random suffix on collision.

Sequential probing (`alexsmith2`, `alexsmith3`) is rejected: it degrades as a base name saturates,
and it makes concurrent signups converge on the same next candidate. Initials are not viable
either; they collide almost immediately.

Collisions are handled by **retrying on unique violation, not by check-then-insert**.
`UserManager.CreateAsync` probes for duplicates, but a probe is not a lock, so two concurrent
signups can both pass it. Both the validator error (`DuplicateUserName`) and the raw 23505 are
handled as the same condition. Full mechanics are in the appendix.

The slug is deliberately ASCII alphanumeric rather than anything `char.IsLetterOrDigit` accepts.
Identity's default `AllowedUserNameCharacters` is ASCII, and a slug that preserved Cyrillic or CJK
would come back as `InvalidUserName`, which the retry loop treats as terminal, leaving that user
unable to create an account at all and unable to correct the name they never chose.

The 3-15 character limit is `RegisterDto` validation only, not a schema constraint, so it does not
bind this path. The generator's 11-character clamp plus a suffix of at most 4 never exceeds 15
anyway, so the two paths agree without a separate rule.

**Consequence accepted knowingly:** `DisplayName` here is not merely a label. It is the public
profile route (`/members/:displayName`, resolved by `UsersController.GetUser` via `FindByNameAsync`)
and the member search key. Sites that auto-generate names freely mostly do not have a unique,
URL-routable username. A Google user's public identity may therefore be `alexsmith3`.

**4.2 `DateOfBirth` is nullable.** This is the schema change the no-completion-page rule forces.
Google will not supply it (it needs the `user.birthday.read` scope and separate consent, and is
frequently hidden or year-less even then). It is only stored and surfaced on the member profile,
and **it is not an age gate**; nothing enforces a minimum age anywhere. A sentinel would have been
worse than null: `default(DateOnly)` renders `0001-01-01` on the profile as though it were a real
birthday.

If an age gate is ever wanted for an auction site (contract capacity), that is a gap in the
*password* signup too, not something this feature introduced.

**4.3 There is no rename endpoint, so a generated name is permanent.** The standard pairing for
auto-generated names is letting users change them from a settings page. `UsersController` is
read-only, and `Description` on `MemberDto` is not settable either. This raises the stakes on 4.1,
and it affects password users equally. Renaming would also mutate a route key, breaking old profile
URLs, which is true of any `DisplayName` edit and independent of federation.

**4.4 No account linking. Email is the account identity.** One email, one account, one auth method.
An email registered with a password cannot sign in via Google, and an email registered via Google
cannot sign in with a password. No auto-linking, no "link your Google account" flow.

This removes the account-takeover vector that auto-linking introduces. It is stricter than most
large sites, which auto-link on a verified provider email. The trade is a UX dead end (§5) for a
materially simpler and safer implementation.

The rule is enforced in three places at increasing depth: `RegisterAsync` rejects a duplicate
email, `options.User.RequireUniqueEmail` backstops it at the Identity layer, and a unique
`EmailIndex` on `NormalizedEmail` backstops that at the database. Only the last is a lock, which is
why the signup path also catches the raw unique violation.

Google sign-in onto an existing password account finds no `AspNetUserLogins` row for the provider
key, then finds a user with that email, and refuses with a reason code.

**4.5 Password-less users are safe by construction.** `CreateAsync(user)` with no password leaves
`PasswordHash` null, and `LoginAsync` goes through `CheckPasswordAsync`, which returns false for a
null hash. This is an inherited property rather than an explicit rule, so it carries a regression
test rather than being left to trust.

**4.6 Refusal messages are explicit, and enumeration is a separate concern.** The refusal rule only
works if the message tells the user what to do instead; "Invalid Credentials" leaves them stuck.
"This email is registered with Google" confirms an account exists, which is an enumeration
disclosure, but the register form already discloses exactly that ("Email is already taken"), so a
precise message does not widen the surface much. The server emits a reason code rather than prose,
so the copy lives in the client and is one string edit to walk back.

**4.7 Returning users are matched on `sub`, not email.** `AspNetUserLogins.ProviderKey` holds
Google's `sub` claim, which is stable; a Google account's email can change. The provider's email is
not synced back onto `AppUser` afterwards: re-syncing could collide with another account's email and
break the one-email-one-account invariant. Our stored email is the identity; theirs only seeded it.

**4.8 `authProvider` identifies federated users to the client.** `AuthService.BuildUserDtoAsync`
reads `AspNetUserLogins` through `UserManager.GetLoginsAsync` and passes the provider into
`AppUserExtensions.ToDto`, which falls back to `"password"` when there is no login row. The provider
is read rather than inferred from a null `PasswordHash`, because the link table is the actual source
of truth for how an account signs in. It is a string rather than a boolean, so a settings page can
render "You sign in with Google" rather than a blank space, and so more providers need no schema
change.

There is no password reset or change flow in the codebase, so nothing needs hiding today. The
constraint this places on any future one is worth stating, because it is easy to get wrong:
**hiding the UI is not the enforcement**. A reset request against a password-less account is the
shape of a takeover attempt, so such an endpoint has to refuse it server-side rather than rely on
the client not offering it.

**4.9 Account lifetime.** The `AppUser` row is created on the first successful callback, one round
trip, no interstitial. Once created the row is permanent and its `Id` is stable; `Auction`, `Bid`,
`Message` and `Payment` all foreign-key to it. Signing back in resolves `sub` to the same
`AppUser.Id`, and therefore the same auctions, bids, messages and orders. This is the practical
reason 4.7 insists on matching by `sub`.

**4.10 `email_verified` is asserted.** Google users have `EmailConfirmed` set to true on creation.
The callback refuses with a reason code when Google supplies an email whose `email_verified` claim
is not true, so an unverified provider email cannot claim an address under the 4.4 rule. Logout is
local only, with no provider sign-out, which is normal.

**Sessions.** Refresh tokens are rows in `RefreshSessions`, one per device, with rotation and reuse
detection. `ExternalLoginAsync` passes the `User-Agent` through to `IssueAuthTokenAsync` the way
`RegisterAsync` and `LoginAsync` do, so federated logins are not the only rows on a sessions page
with no device recorded.

---

## 5. Accepted trade-offs

| Trade-off | Note |
|---|---|
| **Refusal dead end** | The 4.4 rule means a user who forgets which method they used is refused with no self-service recovery. The mitigation is entirely in the error copy, which names the other method. Accepted in exchange for closing the takeover vector. |
| Generated `DisplayName` is a public URL and permanent | No rename endpoint exists (4.3), so whatever is generated is the user's public identity for good. |
| Provider outage | Google users have no password to fall back on and are locked out for its duration. Inherent to the no-linking rule: a real availability dependency rather than a soft degradation. |
| `DisplayName` generation race | Two concurrent signups can pick the same name. Handled by retrying on unique violation rather than check-then-insert (4.1). |

---

## 6. Where the behaviour is pinned

`AuthServiceTests` covers the single-caller rules: a new user creating an account with a generated
name and no date of birth, a name collision suffixing while both users persist, a returning user
matched by `sub` whose provider email changed, refusal when the email belongs to a password
account, refusal when no email claim is supplied, the empty-name fallback to the email local part,
password login against a password-less account, password registration against a Google-held email,
and the lockout branch.

`ExternalLoginConcurrencyTests` covers what a single caller cannot reach. Run serially, Identity's
`UserValidator` absorbs every collision before the INSERT runs, so the savepoint rollback and the
raw 23505 branch are only exercised by concurrent signups against real Postgres: several signups
deriving one name, and two racing for one email.

`DisplayNameGeneratorTests` pins the slug rules, including that every candidate an attempt can
produce stays ASCII alphanumeric and within the register limit.

`ReturnUrlPolicyTests` pins the open-redirect guard in §3.

`AppUserExtensionsTests` pins the `authProvider` mapping in 4.8, including the `"password"` fallback
when no login row exists.
