# Security

How the application protects its users and its data, and what it does not cover. The public demo runs on free
tiers and lets anyone create an account or sign in as a shared demo user, so it is built on the assumption that
some visitors will try to misuse it.

## Accounts and sign-in

| Measure | Details |
| --- | --- |
| Password storage | ASP.NET Core Identity, PBKDF2 with HMAC-SHA512 and a random salt, 210,000 iterations (OWASP guidance). Older hashes are upgraded when their owner signs in. The count is `Security:PasswordHashIterations`. |
| Password rules | At least 12 characters. No "one capital, one symbol" rules, following NIST SP 800-63B, which found they lead to predictable passwords. Refused instead: only digits, runs such as `abcdefghijkl`, well-known words with digits or look-alike letters around them (`Password-2024!`, `P@ssw0rd`), and passwords containing the user's name or e-mail address. |
| Lock-out | Five wrong passwords lock the account for 15 minutes. Wrong two-factor codes and wrong passwords on the account page count too. |
| No hints for attackers | A wrong password, an unknown address, a deactivated account and a locked account all get the same message. Unknown addresses spend the same time as real ones (a decoy hash is checked), so response time does not give them away. |
| Two-factor authentication | Optional, per account. Time-based codes (RFC 6238) from any authenticator app, ten single-use recovery codes, password needed to turn it off or renew the codes. Turning it on or off signs out every other session. |
| Immediate effect of changes | The sign-in cookie is checked against the database on every request. Deactivating a user, changing their role or password ends their sessions at once. |
| Names | Display names may not contain control characters, invisible characters or text-direction overrides, which can make one person look like another. File names get the same treatment. |

## Sessions and requests

- Cookies are `Secure`, `HttpOnly` and `SameSite` (Lax for the session, Strict for the anti-forgery token), and carry the
  `__Host-` prefix, so a browser accepts them only over HTTPS, for this exact host, with no domain attribute.
  Local development over plain http is the one exception.
- Every form post is protected by an anti-forgery token (Razor Pages validate it by default). Signing out is a post too.
- Redirects after sign-in go to local addresses only.
- Session lifetime is eight hours, sliding. "Remember me" is off by default.

## Who may do what

- Everything needs a signed-in user unless a page is explicitly open (sign-in, registration, error pages, static files).
  The default is "deny", not "allow".
- Roles are Customer, Operator and Admin. The administration area is restricted by policy, not by hiding links.
- Access is checked on the object, not only on the page: a customer who asks for someone else's ticket or attachment
  gets "not found", the same answer as for a ticket that does not exist, and the attempt is logged. Internal notes are
  never sent to customers.
- Administrators cannot change their own role or deactivate themselves, so the last administrator cannot lock everyone out.
- **The shared demo accounts are isolated.** Anyone can sign in as the demo operator or administrator with one click, so
  those accounts only ever see and change demo data. They do not see tickets of real visitors, do not see their e-mail
  addresses, and the demo administrator cannot change roles or deactivate accounts. Without this, a visitor could register,
  sign in as the demo administrator and promote their own account.

## Input and output

- Pages are rendered with automatic HTML encoding; there is no raw HTML output of user text.
- All database access goes through Entity Framework with parameters; there is no hand-written SQL.
- Text fields have length limits, searches are limited in length, page numbers are clamped.

## Attachments

- Only listed file types are accepted (images, PDF, text, CSV, Word, Excel), up to 5 files of 5 MB per ticket.
- The type is decided by the extension, never by what the browser claims, **and the beginning of the file must match it**:
  a program renamed to `photo.png` is refused, a text file may not contain binary data.
- Files are stored under random names, in a private Blob container reached through the app's managed identity (no keys).
- Downloads are sent as attachments with `nosniff` and a `default-src 'none'; sandbox` policy, so a browser never runs
  them, whatever they contain.

## HTTP hardening

| Header | Purpose |
| --- | --- |
| `Content-Security-Policy` | Scripts, styles, images and fonts only from the application itself; no inline script or style; no plug-ins; no framing; forms post to this site only. |
| `X-Content-Type-Options`, `X-Frame-Options` | No content sniffing, no clickjacking. |
| `Referrer-Policy`, `Permissions-Policy` | Less information leaves the site; camera, microphone, location and similar features are off. |
| `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy` | Isolates the site from other origins. |
| `Strict-Transport-Security` | One year, outside development. |
| `Cache-Control: no-store` | Pages with private data are not kept by caches or reopened after sign-out. Static files keep their own caching. |

The server software is not announced. The application trusts the Azure front end as the one proxy in front of it, so
the real client address and the original scheme are known.

## Limits on repeated actions

Limits apply per client address (an IPv6 address counts as its whole /64 network). Reading pages is never limited.
They are in the `RequestLimits` configuration section.

| Action | Default |
| --- | --- |
| Password and two-factor code submissions | 10 per minute |
| Registrations | 10 per hour |
| Ticket changes (create, comment, status, assignee, priority) | 60 per 10 minutes |
| File uploads | 20 per hour |

Requests over the limit get status 429 with a `Retry-After` header.

## Logging

Security events are logged with stable event ids: sign-ins, failures, lock-outs, registrations, password and
two-factor changes, role and activation changes by administrators, refused access to tickets, refused uploads, rejected
requests. They name accounts by id. Passwords, codes and e-mail addresses are never written.

## Supply chain

- Dependabot proposes updates for NuGet packages and GitHub Actions every week.
- CodeQL scans the code on every change and weekly.
- `NuGetAudit` runs on every restore, including indirect packages, and warnings are build errors: a package with a known
  vulnerability stops the build.
- Analyzers are errors, formatting is verified in CI, the CI token is read-only.
- The browser libraries in `wwwroot/lib` (Bootstrap, jQuery, jQuery Validation) are copies; check their versions when
  updating the template.

## What is not covered

Known limits, so they are decisions and not surprises:

- **E-mail addresses are not verified and there is no password reset.** Both need outgoing e-mail, which is planned
  together with notifications. Until then the registration form can tell whether an address already has an account;
  the request limit slows down anyone who tries to collect addresses this way.
- **Uploaded files are not scanned for malware.** They are never executed or rendered, but a file may be harmful on
  the machine of the person who opens it. Defender for Storage is not part of the free tier.
- **The demo can be vandalised.** The shared demo accounts can add comments and change demo tickets for the next
  visitor. A scheduled reset of the demo data is planned.
- A two-factor code can be reused inside its 30-second window. Identity does not track used codes.
- Someone who knows an address can lock that account for 15 minutes by failing five times. The per-address limit and the
  same-answer rule keep this from being scaled up cheaply.

## Settings to switch on in GitHub

These are repository settings and cannot be set from the code:

- Branch protection for `main`: pull requests only, required checks, no force pushes.
- Secret scanning with push protection.
- Private vulnerability reporting (used by [SECURITY.md](../SECURITY.md)).
- Dependabot security updates and alerts.

## Reporting a problem

See [SECURITY.md](../SECURITY.md).
