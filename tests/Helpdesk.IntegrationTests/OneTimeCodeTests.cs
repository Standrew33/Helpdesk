using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helpdesk.IntegrationTests;

public class OneTimeCodeTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Email = "nina@example.test";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Runs the service on the test database with a clock the test controls.</summary>
    private async Task<T> WithServiceAsync<T>(Func<OneTimeCodeService, HelpdeskDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return await action(new OneTimeCodeService(db, _clock, Options.Create(new OneTimeCodeOptions())), db);
    }

    private Task<IssuedChallenge> IssueAsync(ChallengePurpose purpose = ChallengePurpose.SignIn)
        => WithServiceAsync(async (codes, _) =>
        {
            var result = await codes.IssueAsync(Email, purpose);
            Assert.Equal(IssueOutcome.Issued, result.Outcome);
            return result.Challenge!;
        });

    private Task<VerifyResult> VerifyAsync(Guid id, string code) => WithServiceAsync((codes, _) => codes.VerifyCodeAsync(id, code));

    private Task<VerifyResult> VerifyTokenAsync(string token) => WithServiceAsync((codes, _) => codes.VerifyTokenAsync(token));

    [Fact]
    public async Task A_correct_code_works_once()
    {
        var issued = await IssueAsync();

        var first = await VerifyAsync(issued.Id, issued.Code);
        var second = await VerifyAsync(issued.Id, issued.Code);

        Assert.Equal(VerifyOutcome.Success, first.Outcome);
        Assert.Equal("NINA@EXAMPLE.TEST", first.Email);
        Assert.Equal(VerifyOutcome.NoLongerValid, second.Outcome);
    }

    [Fact]
    public async Task Codes_are_six_digits_and_spaces_in_the_input_do_not_matter()
    {
        var issued = await IssueAsync();

        Assert.Matches("^[0-9]{6}$", issued.Code);
        Assert.Equal(VerifyOutcome.Success, (await VerifyAsync(issued.Id, $" {issued.Code[..3]} {issued.Code[3..]} ")).Outcome);
    }

    [Fact]
    public async Task Five_wrong_codes_end_the_challenge_even_for_the_right_code()
    {
        var issued = await IssueAsync();
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        for (var left = 4; left >= 1; left--)
        {
            var result = await VerifyAsync(issued.Id, wrong);
            Assert.Equal(VerifyOutcome.WrongCode, result.Outcome);
            Assert.Equal(left, result.AttemptsLeft);
        }

        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyAsync(issued.Id, wrong)).Outcome);
        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyAsync(issued.Id, issued.Code)).Outcome);
    }

    [Fact]
    public async Task A_code_expires_after_ten_minutes()
    {
        var issued = await IssueAsync();

        _clock.Now = _clock.Now.AddMinutes(10).AddSeconds(1);

        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyAsync(issued.Id, issued.Code)).Outcome);
    }

    [Fact]
    public async Task The_link_works_once_and_only_until_it_expires()
    {
        var first = await IssueAsync();
        Assert.Equal(VerifyOutcome.Success, (await VerifyTokenAsync(first.Token)).Outcome);
        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyTokenAsync(first.Token)).Outcome);

        _clock.Now = _clock.Now.AddMinutes(2);
        var second = await IssueAsync();
        _clock.Now = _clock.Now.AddMinutes(11);
        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyTokenAsync(second.Token)).Outcome);
        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyTokenAsync("made-up-token")).Outcome);
    }

    [Fact]
    public async Task Using_the_link_also_uses_up_the_code()
    {
        var issued = await IssueAsync();

        await VerifyTokenAsync(issued.Token);

        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyAsync(issued.Id, issued.Code)).Outcome);
    }

    [Fact]
    public async Task A_new_code_replaces_the_previous_one()
    {
        var old = await IssueAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        var fresh = await IssueAsync();

        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyAsync(old.Id, old.Code)).Outcome);
        Assert.Equal(VerifyOutcome.NoLongerValid, (await VerifyTokenAsync(old.Token)).Outcome);
        Assert.Equal(VerifyOutcome.Success, (await VerifyAsync(fresh.Id, fresh.Code)).Outcome);
    }

    [Fact]
    public async Task A_new_code_has_to_wait_a_minute()
    {
        await IssueAsync();
        _clock.Now = _clock.Now.AddSeconds(20);

        var result = await WithServiceAsync((codes, _) => codes.IssueAsync(Email, ChallengePurpose.SignIn));

        Assert.Equal(IssueOutcome.TooSoon, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(40), result.RetryAfter);
    }

    [Fact]
    public async Task One_address_gets_at_most_five_codes_an_hour()
    {
        for (var i = 0; i < 5; i++)
        {
            await IssueAsync();
            _clock.Now = _clock.Now.AddMinutes(2);
        }

        var sixth = await WithServiceAsync((codes, _) => codes.IssueAsync(Email, ChallengePurpose.SignIn));
        Assert.Equal(IssueOutcome.TooMany, sixth.Outcome);

        _clock.Now = _clock.Now.AddMinutes(51);
        Assert.Equal(IssueOutcome.Issued, (await WithServiceAsync((codes, _) => codes.IssueAsync(Email, ChallengePurpose.SignIn))).Outcome);
    }

    [Fact]
    public async Task Sign_in_and_password_reset_codes_are_separate()
    {
        var signIn = await IssueAsync(ChallengePurpose.SignIn);
        var reset = await IssueAsync(ChallengePurpose.PasswordReset);

        Assert.Equal(ChallengePurpose.PasswordReset, (await VerifyAsync(reset.Id, reset.Code)).Purpose);
        Assert.Equal(VerifyOutcome.Success, (await VerifyAsync(signIn.Id, signIn.Code)).Outcome);
    }

    [Fact]
    public async Task Neither_the_code_nor_the_link_is_stored()
    {
        var issued = await IssueAsync();

        var stored = await WithServiceAsync((_, db) => db.SignInChallenges.AsNoTracking().SingleAsync(c => c.Id == issued.Id));

        Assert.DoesNotContain(issued.Code, stored.CodeHash, StringComparison.Ordinal);
        Assert.NotEqual(issued.Token, stored.TokenHash);
        Assert.Equal(64, stored.CodeHash.Length);
        Assert.Equal(64, stored.TokenHash.Length);
    }
}
