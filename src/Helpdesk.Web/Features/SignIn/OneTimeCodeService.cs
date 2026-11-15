using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.SignIn;

public class OneTimeCodeOptions
{
    public const string SectionName = "OneTimeCodes";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wrong codes allowed for one challenge; after that only a new code helps.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The shortest time between two codes for the same address.</summary>
    public TimeSpan ResendAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Codes for one address per ten minutes, so nobody can flood a mailbox or keep guessing with fresh codes.</summary>
    public int MaxPerAddressPer10Minutes { get; set; } = 3;
}

/// <summary>The code and link to put in the e-mail. They exist only in this object and in the message.</summary>
public sealed record IssuedChallenge(Guid Id, string Code, string Token, DateTimeOffset ExpiresAt);

public enum IssueOutcome
{
    Issued,
    TooSoon,
    TooMany,
}

public sealed record IssueResult(IssueOutcome Outcome, IssuedChallenge? Challenge, TimeSpan RetryAfter);

public enum VerifyOutcome
{
    Success,
    WrongCode,

    /// <summary>Unknown, used, expired, replaced by a newer code, or too many wrong attempts.</summary>
    NoLongerValid,
}

public sealed record VerifyResult(VerifyOutcome Outcome, string? Email = null, ChallengePurpose Purpose = ChallengePurpose.SignIn, int AttemptsLeft = 0);

/// <summary>An open challenge, for the page that asks for its code.</summary>
public sealed record OpenChallenge(Guid Id, string Email, ChallengePurpose Purpose, DateTimeOffset CreatedAt);

/// <summary>
/// Issues six-digit codes and single-use links for e-mail sign-in and password reset, and checks them.
/// A code works once, for a limited time and a limited number of tries; a newer code replaces older ones.
/// </summary>
public class OneTimeCodeService(HelpdeskDbContext db, TimeProvider time, IOptions<OneTimeCodeOptions> options)
{
    /// <summary>The window of the per-address limit.</summary>
    public static readonly TimeSpan LimitWindow = TimeSpan.FromMinutes(10);

    private OneTimeCodeOptions Settings => options.Value;

    public static string Normalise(string email) => email.Trim().ToUpperInvariant();

    public async Task<IssueResult> IssueAsync(string email, ChallengePurpose purpose, CancellationToken ct = default)
    {
        var address = Normalise(email);
        var now = time.GetUtcNow();

        // Old challenges are of no use to anyone; clear them out while we are here.
        await db.Set<SignInChallenge>().Where(c => c.CreatedAt < now.AddDays(-1)).ExecuteDeleteAsync(ct);

        var since = now - LimitWindow;
        var recent = await db.Set<SignInChallenge>()
            .Where(c => c.Email == address && c.Purpose == purpose && c.CreatedAt > since)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count > 0 && recent.Max() + Settings.ResendAfter is var nextAllowed && nextAllowed > now)
        {
            return new IssueResult(IssueOutcome.TooSoon, null, nextAllowed - now);
        }

        if (recent.Count >= Settings.MaxPerAddressPer10Minutes)
        {
            return new IssueResult(IssueOutcome.TooMany, null, recent.Min() + LimitWindow - now);
        }

        // Only the newest code works.
        await db.Set<SignInChallenge>()
            .Where(c => c.Email == address && c.Purpose == purpose && c.ConsumedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now), ct);

        var id = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var expires = now + Settings.Lifetime;

        db.Add(new SignInChallenge
        {
            Id = id,
            Email = address,
            Purpose = purpose,
            CodeHash = HashCode(id, code),
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expires,
        });
        await db.SaveChangesAsync(ct);

        return new IssueResult(IssueOutcome.Issued, new IssuedChallenge(id, code, token, expires), TimeSpan.Zero);
    }

    /// <summary>
    /// Takes back a challenge whose e-mail never went out: it does not count against the address's limits, and
    /// nobody is sent to enter a code that never arrived. Older codes stay replaced; they were already superseded.
    /// </summary>
    public async Task WithdrawAsync(Guid id, CancellationToken ct = default)
        => await db.Set<SignInChallenge>().Where(c => c.Id == id).ExecuteDeleteAsync(ct);

    /// <summary>The newest challenge for the address that still works, for when someone asks again too soon.</summary>
    public async Task<Guid?> LatestOpenAsync(string email, ChallengePurpose purpose, CancellationToken ct = default)
    {
        var address = Normalise(email);
        var now = time.GetUtcNow();

        return await db.Set<SignInChallenge>()
            .Where(c => c.Email == address && c.Purpose == purpose && c.ConsumedAt == null && c.ExpiresAt > now
                && c.FailedAttempts < Settings.MaxAttempts)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The challenge a link belongs to, without using it up: the link page asks for a click first.</summary>
    public async Task<OpenChallenge?> FindOpenByTokenAsync(string token, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var hash = Hash(token ?? string.Empty);
        var challenge = await db.Set<SignInChallenge>().AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == hash, ct);

        return challenge is null || !IsOpen(challenge, now)
            ? null
            : new OpenChallenge(challenge.Id, challenge.Email, challenge.Purpose, challenge.CreatedAt);
    }

    /// <summary>A challenge in any state, so a new code can be sent for the same address after the old one ran out.</summary>
    public async Task<OpenChallenge?> FindAsync(Guid id, CancellationToken ct = default)
        => await db.Set<SignInChallenge>().AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new OpenChallenge(c.Id, c.Email, c.Purpose, c.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<OpenChallenge?> FindOpenAsync(Guid id, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var challenge = await db.Set<SignInChallenge>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

        return challenge is null || !IsOpen(challenge, now)
            ? null
            : new OpenChallenge(challenge.Id, challenge.Email, challenge.Purpose, challenge.CreatedAt);
    }

    public async Task<VerifyResult> VerifyCodeAsync(Guid id, string code, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var challenge = await db.Set<SignInChallenge>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

        if (challenge is null || !IsOpen(challenge, now))
        {
            return new VerifyResult(VerifyOutcome.NoLongerValid);
        }

        var entered = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (!SameHash(challenge.CodeHash, HashCode(id, entered)))
        {
            // Counted in the database itself, so parallel guesses cannot slip past the limit.
            await db.Set<SignInChallenge>().Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), ct);

            var left = Settings.MaxAttempts - challenge.FailedAttempts - 1;
            return left > 0 ? new VerifyResult(VerifyOutcome.WrongCode, AttemptsLeft: left) : new VerifyResult(VerifyOutcome.NoLongerValid);
        }

        return await ConsumeAsync(challenge, now, ct);
    }

    public async Task<VerifyResult> VerifyTokenAsync(string token, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var hash = Hash(token ?? string.Empty);
        var challenge = await db.Set<SignInChallenge>().AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == hash, ct);

        return challenge is null || !IsOpen(challenge, now)
            ? new VerifyResult(VerifyOutcome.NoLongerValid)
            : await ConsumeAsync(challenge, now, ct);
    }

    /// <summary>Marks the challenge used. Only one of two simultaneous requests can win.</summary>
    private async Task<VerifyResult> ConsumeAsync(SignInChallenge challenge, DateTimeOffset now, CancellationToken ct)
    {
        var updated = await db.Set<SignInChallenge>()
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        return updated == 1
            ? new VerifyResult(VerifyOutcome.Success, challenge.Email, challenge.Purpose)
            : new VerifyResult(VerifyOutcome.NoLongerValid);
    }

    private bool IsOpen(SignInChallenge challenge, DateTimeOffset now)
        => challenge.ConsumedAt is null && challenge.ExpiresAt > now && challenge.FailedAttempts < Settings.MaxAttempts;

    // A six-digit code has only a million values, so it is bound to its challenge before hashing:
    // a hash cannot be reused for another challenge, and a stored hash alone is no working code.
    private static string HashCode(Guid id, string code) => Hash($"{id:N}:{code}");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SameHash(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
