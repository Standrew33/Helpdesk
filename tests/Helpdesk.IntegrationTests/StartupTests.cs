using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Helpdesk.IntegrationTests;

public class StartupTests(SqlServerFixture sql) : WebTestBase(sql)
{
    [Fact]
    public void Production_does_not_start_without_an_e_mail_server()
    {
        using var production = Factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        var error = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("Email:Host", error.ToString(), StringComparison.Ordinal);
    }
}
