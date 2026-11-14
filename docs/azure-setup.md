# Deploying to Azure (free tiers)

The demo runs on an App Service **F1** plan (Linux) with an Azure SQL Database from the
**free offer**. Deployment is done by the `Deploy` GitHub Actions workflow, which signs in
to Azure with OpenID Connect, so no passwords or publish profiles are stored in GitHub.

Free-tier limits worth knowing:

- F1 has 60 CPU minutes per day, no Always On and no custom domain with TLS. The app goes
  to sleep when idle and the first request after that is slow.
- The Azure SQL free offer gives 100,000 vCore seconds and 32 GB per month. Keep the default
  "auto-pause the database until next month" when the allowance runs out, so nothing is billed.
  A paused serverless database needs up to a minute to resume, which the app tolerates
  (connection timeout and retries).

## 1. Resource group, plan and web app

```powershell
az login
az webapp list-runtimes --os-type linux      # confirm the exact .NET 10 runtime name

$rg  = "rg-helpdesk"
$loc = "westeurope"
$app = "helpdesk-<something-unique>"

az group create -n $rg -l $loc
az appservice plan create -g $rg -n plan-helpdesk --is-linux --sku F1
az webapp create -g $rg -p plan-helpdesk -n $app --runtime "DOTNETCORE:10.0"
```

## 2. Database

Create it in the portal (the free offer is applied there): Azure SQL, Create SQL Database,
choose "Apply offer" at the top of the form. Use SQL authentication for the demo, keep
"Allow Azure services and resources to access this server" enabled, and put the database in
the same region as the web app. Note the server admin login and password.

## 3. App configuration

Set the connection string in the portal (Web App, Settings, Environment variables,
Connection strings): name `Default`, type `SQLAzure`, value

```
Server=tcp:<server>.database.windows.net,1433;Database=<db>;User ID=<login>;Password=<password>;Encrypt=True;Connect Timeout=60
```

Then the application settings:

```powershell
az webapp config appsettings set -g $rg -n $app --settings `
  Database__MigrateOnStartup=true Demo__Enabled=true Demo__SeedUsers=true Demo__SeedTickets=true
```

On startup the app applies migrations, creates roles, demo users and sample tickets
(only when the database has no tickets).

## 4. Let GitHub deploy (OpenID Connect)

```powershell
$sub    = az account show --query id -o tsv
$tenant = az account show --query tenantId -o tsv
$appId  = az ad app create --display-name "gh-helpdesk-deploy" --query appId -o tsv
az ad sp create --id $appId

@'
{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Standrew33/Helpdesk:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}
'@ | Set-Content credential.json
az ad app federated-credential create --id $appId --parameters credential.json
Remove-Item credential.json

$scope = az webapp show -g $rg -n $app --query id -o tsv
az role assignment create --assignee $appId --role "Website Contributor" --scope $scope
```

In the repository, Settings, Secrets and variables, Actions, **Variables**, add:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | `$appId` |
| `AZURE_TENANT_ID` | `$tenant` |
| `AZURE_SUBSCRIPTION_ID` | `$sub` |
| `AZURE_WEBAPP_NAME` | the web app name |

## 5. Deploy

Run the `Deploy` workflow once from the Actions tab (Run workflow). After that every
push to `main` is deployed once CI has passed. The site is at `https://<app>.azurewebsites.net`.

## 6. Attachment storage (Blob Storage)

Ticket attachments are kept in a private Blob Storage container. The web app reaches it with its
own managed identity, so no keys or connection strings are stored anywhere. Without these
settings the app keeps attachments in a local folder (development and tests only).

Limits enforced by the application: 5 files per ticket, 5 MB each, a short list of file types
(images, PDF, text, CSV, Word, Excel). This keeps storage use, and cost, very small.

1. Create a **Storage account** in the portal: same resource group and region as the web app,
   Performance **Standard**, Redundancy **LRS**, access tier **Hot**. On the Advanced tab leave
   anonymous access disabled and keep minimum TLS version 1.2.
2. Web App, Settings, **Identity**, tab *System assigned*: turn it **On** and save.
3. Storage account, **Access Control (IAM)**, Add role assignment: role **Storage Blob Data
   Contributor**, assign access to *Managed identity*, select the web app. The role can take a few
   minutes to apply.
4. Web App, Settings, **Environment variables**, App settings, add
   `BlobStorage__AccountUrl` = `https://<storage account name>.blob.core.windows.net`.
   The container (`attachments`, private) is created by the app on the first upload.
5. In the Azure portal create a budget alert (Cost Management, Budgets) so any unexpected cost is
   noticed early.

## 7. Google Search Console (optional)

Browsers can show a "dangerous site" warning for a host on the shared `azurewebsites.net` domain.
To ask Google to review it, add the site in Search Console as a *URL prefix* property, choose the
**HTML tag** verification method and copy the value of the `content` attribute. Put it into an app
setting of the web app, `Site__GoogleSiteVerification`, and press Verify in Search Console.
The application then writes the `google-site-verification` meta tag into every page.

## 8. E-mail (required)

Sign-in and password reset send a code and a link by e-mail, so the app needs an SMTP server and
**does not start in production without one**. Any provider with an SMTP relay works; on the free
tiers, Brevo (300 messages a day) and Resend (3,000 a month) are good fits. Both need the sending
domain verified with a few DNS records (SPF, DKIM), otherwise messages end up in spam, so send
from an address on a domain you control, for example `helpdesk@<your-domain>`.

```powershell
az webapp config appsettings set -g $rg -n $app --settings `
  Email__Host=<smtp-host> Email__Port=587 Email__User=<smtp-user> Email__Password=<smtp-password> `
  Email__From=helpdesk@<your-domain> Email__FromName=Helpdesk
```

Port 587 uses STARTTLS, port 465 TLS from the start; plain connections are never used.

Links in the e-mails point to `Site:PublicUrl`, which is set in `appsettings.Production.json` to the
`azurewebsites.net` address. With a custom domain, override it with the app setting
`Site__PublicUrl=https://<your-domain>`. The application never takes the address from the request,
so a forged `Host` header cannot put another site into someone's sign-in e-mail.

## 9. reCAPTCHA (recommended)

The sign-in, password and password reset forms are checked with Google reCAPTCHA v3. Register the
site in the reCAPTCHA admin console (type **v3**, add the `azurewebsites.net` host and any custom
domain), then:

```powershell
az webapp config appsettings set -g $rg -n $app --settings `
  Captcha__SiteKey=<site-key> Captcha__SecretKey=<secret-key>
```

`Captcha__MinimumScore` (default `0.5`) is the lowest score accepted. Without both keys the check
is off and the app logs a warning at start-up; the per-address request limits still apply.
