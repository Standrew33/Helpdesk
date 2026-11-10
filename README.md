# Helpdesk

A small ticketing system built with ASP.NET Core Razor Pages, EF Core and SQL Server.

Work in progress.

## Planned scope

- Tickets with a lifecycle (new, in progress, waiting for customer, closed), priorities, assignee and SLA deadlines
- Roles: customer, operator, admin (policy-based authorization)
- Comments and attachments (Azure Blob Storage)
- Audit log of ticket changes
- E-mail notifications on status change
- xUnit tests (business rules and integration tests)
- CI on GitHub Actions

## Configuration

Copy `src/Helpdesk.Web/appsettings.example.json` and fill in the values using
user secrets or environment variables. Do not commit secrets.

## Build and test

```
dotnet build
dotnet test
```
