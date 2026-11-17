# Helpdesk

[![CI](https://github.com/Standrew33/Helpdesk/actions/workflows/ci.yml/badge.svg)](https://github.com/Standrew33/Helpdesk/actions/workflows/ci.yml)

A help desk service built with ASP.NET Core Razor Pages, EF Core and SQL Server. Everyone who
signs up gets a help desk of their own: they invite their team by e-mail, let customers in
through a public link, and work the requests against priority deadlines.

Work in progress.

## Planned scope

- Separate help desks (workspaces) in one application, isolated from each other in every query
- A platform owner (superadmin) who looks after every help desk, set by configuration
- Tickets with a lifecycle (new, in progress, waiting for customer, closed), priorities, assignee and SLA deadlines
- Roles within a help desk: customer, operator, admin (policy-based authorization)
- Comments and attachments (Azure Blob Storage)
- Audit log of ticket changes
- E-mail notifications on status change
- xUnit tests (business rules and integration tests)
- CI on GitHub Actions

## Structure

```
src/
  Helpdesk.Core   domain model and business rules, no dependencies
  Helpdesk.Web    Razor Pages, EF Core, Identity, Blob Storage, e-mail
tests/
  Helpdesk.Core.Tests          unit tests for business rules
  Helpdesk.IntegrationTests    web app tests against a real SQL Server
```

## Configuration

Copy `src/Helpdesk.Web/appsettings.example.json` and fill in the values using
user secrets or environment variables. Do not commit secrets.

Useful settings: `Database:MigrateOnStartup` applies migrations at startup (always on in
Development), `Platform:OwnerEmail` names the owner of the platform, the superadmin who looks
after every help desk.

## Deployment

The application runs on Azure free tiers and is deployed by GitHub Actions after CI passes.
See [docs/azure-setup.md](docs/azure-setup.md).

## Build and test

```
dotnet build
dotnet test
```

Integration tests run against a real SQL Server. By default they start a SQL Server
container (Docker is required). To use an existing server instead, for example LocalDB,
set the connection string without a database name:

```
$env:HELPDESK_TEST_SQL = "Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test
```

Each test creates its own temporary database and removes it afterwards.


