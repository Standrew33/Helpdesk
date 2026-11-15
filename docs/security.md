# Security

How the application protects its users and its data, and what it does not cover. The public site
lets anyone sign up and offers shared demo accounts, so it is built on the assumption that some
visitors will try to misuse it.

## Signing in and signing up

Sign-in starts with the e-mail address only. A six-digit code and a single-use link go to that
address, and the person either enters the code or opens the link. There is no separate sign-up
form: an address without an account continues to a welcome step for the name, and the account is
created only then.

| Measure | Details |
| --- | --- |
| Address ownership | Every account belongs to an address that received a code, so nobody can register someone else's address. |
| No account discovery | Asking for a code, asking for a password reset and a failed password sign-in answer the same way for known and unknown addresses. Unknown and locked accounts spend the time of a real password check, so response time gives nothing away either. |
| Codes and links | Six digits or 256 random bits, valid once and for ten minutes; a newer code replaces older ones; five wrong codes end the code. Only SHA-256 hashes are stored, the code hash bound to its challenge. Use and wrong tries are counted in the database itself, so parallel requests cannot get past the limits. |
| Link scanners | Opening the link only shows a confirmation button. Mail scanners and link previews that open links on their own neither sign anyone in nor use the link up. |
| Links point home | Links are built from the configured `Site:PublicUrl`, never from the request's `Host` header, so a forged header cannot plant another site's address in someone's e-mail (password reset poisoning). |
| Passwords | Optional: a second way in, set on the welcome step or later in the settings. At least 8 characters, no "one capital, one symbol" rules (NIST SP 800-63B), but refused: only digits, runs such as `abcdefgh`, fewer than five different characters, well-known words with digits or look-alike letters around them (`P@ssw0rd-2024`) and passwords that contain the user's name or e-mail address. A meter shows the strength while typing and applies the same rules. |
| Password storage | ASP.NET Core Identity, PBKDF2 with HMAC-SHA512 and a random salt, 210,000 iterations (OWASP). Older hashes are upgraded at the next sign-in. |
| Lock-out | Five wrong passwords lock the account for 15 minutes; wrong current passwords on the account page count too. The e-mail code still works, and a password reset lifts the lock. |
| Password reset | A reset code or link leads to a new-password step; the code alone signs nobody in. Saving signs the account out everywhere else. Reset and sign-in codes cannot be swapped. |
| Bots | Google reCAPTCHA v3 checks the address, password and reset forms (score threshold, matching action, refused when Google cannot be reached). The forms also have a hidden field that only programs fill in. |
| Changes apply at once | The sign-in cookie is checked against the database on every request. Deactivating a user, changing their role, password or resetting the password ends their other sessions immediately. |
| Names | Display names and file names may not contain control characters, invisible characters or text-direction overrides, which let one person pose as another. |

## Sessions and requests

- Cookies are `Secure`, `HttpOnly` and `SameSite` (Lax for the session, Strict for the anti-forgery
  token) and carry the `__Host-` prefix, so a browser accepts them only over HTTPS, for this exact
  host. Local development over plain http is the one exception.
- A proven address waiting for the welcome or new-password step is held in a cookie that is
  signed, encrypted and expires after 15 minutes (ASP.NET Core data protection).
- Every form post carries an anti-forgery token. Signing out is a post too.
- Redirects after sign-in go to local addresses only.

## Who may do what

- Everything needs a signed-in user unless a page is explicitly open (sign-in steps, error pages,
  static files). The default is "deny".
- Roles are Customer, Operator and Admin. The administration area is restricted by policy.
- Access is checked on the object: a customer who asks for someone else's ticket or attachment
  gets "not found", like a ticket that does not exist, and the attempt is logged. Internal notes
  are never sent to customers.
- Administrators cannot change their own role or deactivate themselves.
- The shared demo accounts see and change demo data only. They do not see real visitors' tickets
  or e-mail addresses, and the demo administrator cannot change roles or deactivate anyone.
  Without this, a visitor could sign up, sign in as the demo administrator and promote their own
  account.

## Input and output

- Pages are rendered with automatic HTML encoding; user text is never written as raw HTML.
- All database access goes through Entity Framework with parameters; there is no hand-written SQL.
- Text fields have length limits, searches are limited in length, page numbers are clamped.

## Attachments

- Only listed file types (images, PDF, text, CSV, Word, Excel), up to 5 files of 5 MB per ticket.
- The type comes from the extension, never from the browser, and the start of the file must match
  it: a program renamed to `photo.png` is refused, a text file may not contain binary data.
- Files are stored under random names in a private Blob container reached with the app's managed
  identity (no keys).
- Downloads are attachments with `nosniff` and a `default-src 'none'; sandbox` policy.

## HTTP hardening

| Header | Purpose |
| --- | --- |
| `Content-Security-Policy` | Scripts, styles, images and fonts from the site only; no inline code, no plug-ins, no framing, forms post to the site only. The three reCAPTCHA pages use Google's recommended policy instead: only scripts with the response's random nonce run, plus the scripts they add (`'strict-dynamic'`), and Google's frame is allowed. |
| `X-Content-Type-Options`, `X-Frame-Options` | No content sniffing, no clickjacking. |
| `Referrer-Policy`, `Permissions-Policy` | Less information leaves the site; camera, microphone, location and similar features are off. |
| `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy` | Isolate the site from other origins. |
| `Strict-Transport-Security` | One year, outside development. |
| `Cache-Control: no-store` | Pages with private data are not kept by caches. Static files keep their own caching. |

The server software is not announced and request bodies are limited to 1 MB, except uploads. The
Azure front end is trusted as the one proxy, so the real client address and scheme are known.

## Limits on repeated actions

Per client address (an IPv6 address counts as its /64 network); reading pages is never limited.
They are in the `RequestLimits` configuration section. Over the limit, the post never reaches its
handler: the form is shown again with what was typed and the waiting time as a form error, with
status 429 and `Retry-After`.

| Action | Default |
| --- | --- |
| E-mails with a code (sign-in, new code, password reset) | 10 per hour |
| Sign-in forms (code, link, password, new password, account page) | 10 per minute |
| New accounts | 10 per hour |
| Ticket changes | 60 per 10 minutes |
| File uploads | 20 per hour |

Each e-mail address also gets at most one code a minute and five an hour (`OneTimeCodes` section).

## Logging

Security events have stable event ids: sign-ins and failures, lock-outs, sign-ups, password
changes, role and activation changes, refused ticket access, refused uploads, CAPTCHA refusals,
rejected requests. Accounts are named by id; passwords, codes and e-mail addresses are never
written.

## Supply chain

- Dependabot proposes updates for NuGet packages and GitHub Actions every week.
- CodeQL scans the code on every change and weekly.
- `NuGetAudit` checks direct and indirect packages on every restore; a known vulnerability fails
  the build.
- Analyzers are errors, formatting is verified in CI, the CI token is read-only.

## What is not covered

- **Uploaded files are not scanned for malware.** They are never executed or rendered by the site,
  but a file may still harm the computer of whoever opens it.
- **The demo can be vandalised.** The demo accounts can add comments and change demo tickets for
  the next visitor. The demo accounts are to be replaced by an interactive preview.
- Someone who knows an address can lock its password sign-in for 15 minutes by failing five
  times; the owner can still sign in with an e-mail code.
- reCAPTCHA sends data about the visit to Google; that is the price of the check.

## Settings to switch on in GitHub

- Branch protection for `main`: pull requests only, required checks, no force pushes.
- Secret scanning with push protection.
- Private vulnerability reporting (used by [SECURITY.md](../SECURITY.md)).
- Dependabot alerts and security updates.
