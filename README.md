# Book Library (demo)

ASP.NET Core MVC + Identity + PostgreSQL (Supabase).


A site for reading books and for sending in Bulgarian translations of books that have none. Visitors browse and search the library. Logged-in users submit translations as .docx or PDF files. An admin reviews each one and approves or rejects it, and approved translations are published on the book page. Admins also add, edit and delete books.

## Database
The app uses PostgreSQL. There is no database address or password in the code. Each person sets the connection string once, with user secrets (stored on your PC, outside the repo). Pick one:

**Option 1: Supabase** (recommended for the team and for hosting)
1. Create a project at supabase.com and set a database password.
2. In the project, click **Connect** and copy the **Direct connection** string (or the **Session pooler** one if your network has no IPv6). It looks like `postgresql://postgres:[PASSWORD]@db.xxxx.supabase.co:5432/postgres`. Do not use the Transaction pooler (port 6543) for this app: it applies migrations at startup, which needs a normal session.
3. Put your real password in place of `[PASSWORD]` and save the string:

       dotnet user-secrets set "ConnectionStrings:Default" "postgresql://postgres:YOUR_PASSWORD@db.xxxx.supabase.co:5432/postgres"

   Connections to remote databases are encrypted automatically. Paste the password as it is, including symbols such as `@`, `#` or `/`. Only a literal `%` must be written as `%25`. Do not keep the square brackets around `[YOUR-PASSWORD]` from Supabase's example. In PowerShell put the whole string in single quotes.

**Option 2: Postgres on your own PC**

    dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=booklibrary;Username=postgres;Password=YOUR_PASSWORD"

Create the empty database first (`createdb booklibrary`); the app creates the tables itself.

**On a server** set the environment variable `ConnectionStrings__Default` instead.

Everyone who uses the same connection string shares the same data (books, users, translations). Use a separate Supabase project, or Option 2, if you want your own test data.

## Run
    dotnet run
Open https://localhost:7080. On first start the app creates the tables and seeds 8 books. Without a database setting it stops with a message that says what to set.
Targets .NET 10; for an older SDK change `TargetFramework` and the `10.0.*` package versions in BookLibrary.csproj.

In Visual Studio, open `BookLibrary.csproj`, pick the **BookLibrary** profile and press F5. If the browser warns about the HTTPS certificate, run `dotnet dev-certs https --trust` once.

## Accounts
- Register / Log in: the Identity pages under the top-right menu.
- The admin account is not in the code. Each person sets their own once, with user secrets (stored on your PC, outside the repo):

      dotnet user-secrets set "Admin:Email" "you@example.com"
      dotnet user-secrets set "Admin:Password" "YourPassword1!"

  The password needs upper and lower case, a digit and a symbol. Restart the app and the account is created.
  If the email belongs to an account that already exists, that account becomes admin (its password is not changed).
  The password setting is only used when the account is first created. To change an existing password, use the Password tab under your email at the top right.
- On a server, set the environment variables `Admin__Email` and `Admin__Password` instead.
- Without these settings the app still runs, but no admin is created (a warning is logged at startup).

## Hosting on Render
The repo has a `Dockerfile`, so Render (or any host that runs containers) can build and run the app.

1. In Render, create a **Web Service** from this GitHub repo. Choose the **Docker** runtime and the branch to deploy (`main`).
2. Add these environment variables in Render's dashboard (type the values there; never put them in the repo or in chat):
   - `ConnectionStrings__Default`: your Supabase **Session pooler** string (see Database above)
   - `Admin__Email` and `Admin__Password`: the admin account to create on the first start
   - `Storage__Supabase__Url` and `Storage__Supabase__Key`: where admin uploads are kept (see Uploads below). Without them uploads are lost on every deploy.
3. Deploy. Render sets the `PORT` variable and the app listens on it. Open the service's `onrender.com` address and log in as the admin.

How it behaves on a host:
- The app runs in Production mode, as a non-root user, behind Render's HTTPS proxy (the `X-Forwarded-*` headers are trusted).
- Login cookies and form tokens use keys kept in the database (table `DataProtectionKeys`), so people stay logged in after a redeploy or a restart.
- Uploaded covers and PDFs are kept in Supabase Storage when it is configured (see Uploads below). If it is not, they go to the container's disk and are lost on every deploy. The seeded covers and PDFs are part of the image and are not affected.
- On Render's free plan the service goes to sleep when nobody visits, so the first request afterwards can take about a minute.
- Every push to the deployed branch redeploys automatically, and new migrations are applied when the app starts.

To try the image on your own PC (needs Docker):

    docker build -t booklibrary .
    docker run -p 10000:10000 -e PORT=10000 -e "ConnectionStrings__Default=..." -e Admin__Email=you@example.com -e "Admin__Password=..." booklibrary

## Uploads (covers and PDFs)
Admin uploads go to **Supabase Storage** when these two settings are present, and to the local `wwwroot/uploads/` folder when they are not (fine on your own PC).

| Setting | Value |
|---|---|
| `Storage:Supabase:Url` | `https://PROJECTREF.supabase.co` (Supabase: **Project Settings > API**, the project URL) |
| `Storage:Supabase:Key` | a **secret** key (`sb_secret_...`) from **Project Settings > API Keys**, or the legacy `service_role` key |
| `Storage:Supabase:Bucket` | optional, default `uploads` |

On your PC set them with `dotnet user-secrets set "Storage:Supabase:Url" "..."` (and the same for the key); on Render use `Storage__Supabase__Url` and `Storage__Supabase__Key`.

- The secret key gives full access to the project and skips all access rules. Keep it on the server: only in user secrets and Render's environment, never in the repo, the browser or a chat.
- On start the app creates the bucket as a **public** bucket if it does not exist, and logs `Uploads: Supabase Storage bucket "uploads" ...`. Public means anyone with a file's link can open it, which is what covers and the English PDFs need.
- If the bucket already exists but is private, the app logs a warning: make it public in Supabase (Storage > the bucket > Edit bucket).
- Files get random names. Replacing or removing a cover or PDF, or deleting a book, deletes its old file from the bucket. If storage refuses an upload (for example a file larger than the project allows), the admin sees a message and the book is not changed.
- Books whose files were uploaded before this was set up point at `/uploads/...` files that may no longer exist; upload those covers again.

## Email (optional)
The site sends email through **Resend** (resend.com), using its web API. Render's free plan blocks the normal email (SMTP) ports, so an SMTP server would not work there.

| Setting | Value |
|---|---|
| `Email:Resend:ApiKey` | a Resend API key (Resend: **API Keys > Create API Key**, "Sending access") |
| `Email:From` | the sender, e.g. `Book Library <noreply@yourdomain.com>` |
| `Email:RequestsPer15Minutes` | optional, default 10 (see below) |

On Render use `Email__Resend__ApiKey` and `Email__From`; on your PC, `dotnet user-secrets set "Email:Resend:ApiKey" "..."`. Never put the key in the repo or in chat.

**A domain is needed to email anyone.** Resend only sends to other people from an address on a domain you own and have verified in Resend (**Domains > Add Domain**, then add the DNS records it shows at your domain registrar). Without a domain you can test with `Email:From` set to `Book Library <onboarding@resend.dev>`, but then Resend only delivers to the email address of your own Resend account.

With both settings present:
- **New accounts must confirm their email address** before they can log in. Registering sends a link; "Resend email confirmation" on the login page sends it again. Accounts that existed before this feature (migration `ConfirmExistingUsers`) count as confirmed, and an admin can confirm anyone by hand on the Users page.
- **"Forgot your password?" emails a reset link** (works once, expires after a day). The page gives the same answer whether or not the address has an account, and blocked accounts get no link.
- The pages that send email (register, forgot password, resend confirmation) accept at most `Email:RequestsPer15Minutes` requests per IP address per 15 minutes, so nobody can use the site to flood an inbox.

Without the settings nothing is sent: accounts work without confirmation and "Forgot your password?" tells people to ask an admin, who can create a reset link on the Users page.

## Google login (optional)
1. Google Cloud Console > APIs & Services > Credentials > Create OAuth client ID (Web application).
2. Authorized redirect URI: `https://localhost:7080/signin-google`
3. Then:
       dotnet user-secrets set "Google:ClientId" "YOUR_ID"
       dotnet user-secrets set "Google:ClientSecret" "YOUR_SECRET"
4. Restart. A "Google" button appears on the Log in and Register pages. Without keys it stays hidden.
(Not tested yet.)

## Features

**Everyone**
- Library home page: books grouped by nationality, with covers. Search by title or author and filter by nationality. Matching ignores case, Cyrillic included.
- Book page: author, year, original language, summary, **Read in English** (PDF, shown only if the book has one), and **Read in Bulgarian** once a translation is approved.
- Accounts: after 5 wrong passwords in a row an account is locked for 10 minutes. With email set up (see Email), new accounts confirm their email address and "Forgot your password?" emails a reset link; without it, an admin creates reset links.

**Logged-in users**
- **Translate to Bulgarian**: upload a .docx or a text-based PDF.
- One translation per book can wait for review at a time. While it waits, the book page shows its status instead of the Translate button.
- **My translations** (top menu): every submission with its status: waiting for review, published, rejected (with the admin's reason) or replaced by a newer translation. After a rejection, **Send a new version**.

**Admins** (menu items appear only for admins)
- **Translation requests**: read a submission and approve or reject it, with an optional reason for rejecting.
  - A translation is decided only once; deciding it again (double click, second tab, back button) changes nothing.
  - Only one approved translation per book. Approving a new one replaces the old one, and the review page warns before doing so.
- **Manage books**: add, edit and delete books.
  - Fields: title, author, nationality (suggests existing ones), year first published, original language, summary.
  - **Hidden**: a hidden book is left out of the library and search, and only admins can open its page. Use it to prepare a book before publishing it.
  - Optional cover image (.png, .jpg, .webp) and English PDF. SVG covers are not accepted because they can contain scripts.
  - Uploads are saved in Supabase Storage (or `wwwroot/uploads/` without it, see Uploads) under random names. PDFs must start with the `%PDF` header. Replaced or deleted uploads are removed from disk. The seeded files in `wwwroot/covers` and `wwwroot/pdfs` are never deleted.
  - Deleting a book also deletes its translations; the confirmation page says how many.
- **Users**: search users by email and see their role, status and number of translations.
  - **Make admin / Remove admin.** You can't remove your own role, the last admin keeps the role, and the admin from the `Admin:Email` setting is protected (it gets the role back on every start).
  - **Block / Unblock.** A blocked user is logged out within a minute and cannot log in. Admins must lose the role before they can be blocked. **Unblock** also clears a lockout after wrong passwords.
  - **Password reset link** for a user who forgot their password: copy it and send it to them yourself. It works once and expires after a day.

## Flow to try
1. Home: click a nationality to expand it and see covers, click a book. Use the search box to find a book by title or author, or filter by nationality.
2. A book with no Bulgarian translation shows **Read in English** (opens the PDF) and **Translate to Bulgarian**.
3. Log in as a normal user, press Translate, upload `test-files/sample-bulgarian-translation.docx`.
4. Log in as admin, open **Translation requests**, read the text, Approve or Reject (optionally with a reason).
5. After approval the book shows **Read in Bulgarian**. Approving another translation of the same book replaces it.
6. The translator follows their submissions on **My translations**: waiting, published, rejected (with the reason) or replaced. After a rejection they can send a new version.
7. As admin, open **Manage books** and add a book with a cover and a PDF, edit it, then delete it. Tick **Hidden** and check that the book disappears from the library for visitors.
8. As admin, open **Users**, make someone an admin, block and unblock a user, and create a password reset link.

## Project layout
- `Controllers/`: `HomeController` (library and search), `BooksController` (book page, submit translation, My translations), `AdminController` (translation review), `AdminBooksController` (book management), `AdminUsersController` (users)
- `Models/`: `Book`, `Translation` and `TranslationStatus` (Pending, Approved, Rejected, Replaced), `BookForm` (admin add/edit form)
- `Dockerfile`, `.dockerignore`: build and run the app in a container (Render)
- `Services/FileStorage.cs`: where admin uploads are kept (Supabase Storage or the local folder)
- `Services/LockoutSignInManager.cs`: counts wrong passwords so accounts lock after 5
- `Areas/Identity/Pages/Account/ForgotPassword.cshtml`: replaces the built-in forgot-password page
- `tests/BookLibrary.Tests/`: automated tests; `.github/workflows/tests.yml` runs them on GitHub
- `Data/`: `ApplicationDbContext`, `DatabaseConfig` (reads the connection string), `SeedData` (migrations, sample books, admin account), `Migrations/`
- `Views/`: Razor pages for each controller
- `wwwroot/`: seeded covers and PDFs, plus `uploads/` (created at run time, not in git)
- `test-files/`: a sample .docx translation

## Notes
- English PDFs in `wwwroot/pdfs` are placeholders; replace each with the real file (same name).
- Translation uploads must be .docx or a text-based PDF (scans cannot be read). A .docx with no readable body is treated as unreadable instead of crashing.
- Without Supabase Storage settings, uploads go to `wwwroot/uploads/` on the machine that uploaded them; they are not in git and not in the database.
- **Row-level security is turned on automatically.** Supabase exposes tables in the `public` schema through its Data API, which anyone holding the project's public key can call. On every start the app turns on row-level security (with no policies, which blocks that access) for any table that does not have it yet, including tables added by future migrations, and logs the table names. The app itself is not affected because it connects as the table owner (Supabase's `postgres` user). Do not connect with a different, non-owner database user: row-level security would hide the tables from it. If the connected user may not change the tables, the app logs a warning and starts anyway; then run `ALTER TABLE public."TableName" ENABLE ROW LEVEL SECURITY;` yourself in the SQL Editor.
- The database is updated with EF Core migrations (in `Data/Migrations`). The app applies any new ones when it starts, so there is no need to reset the database after a model change.

## Tests
The tests start the real app against a new, empty PostgreSQL database for each group of tests and delete it afterwards, so they never touch the Supabase data. Uploads go to memory.

They need a PostgreSQL server. By default they use `localhost:5432` with user and password `postgres`; for another server set `TEST_DATABASE` to its connection string (without a database name). Then run, in the project folder:

    dotnet test tests/BookLibrary.Tests

GitHub runs them automatically on every pull request and every push to `main` (workflow `.github/workflows/tests.yml`, with its own PostgreSQL). A red mark on a pull request means a test failed: fix it before merging. When you add a feature, add a test for it in `tests/BookLibrary.Tests`.

## Changing the data model
After changing a model (for example adding a field to `Book`), create a migration and commit it with your change:

    dotnet tool restore
    dotnet ef migrations add AddBookYear --output-dir Data/Migrations

Use a name that says what changed. `dotnet tool restore` is only needed once per PC.
The command needs the database setting (see Database above), so it works once your user secret is set.
Start the app and the change is applied to the database. If the team shares one database, the first person to start the new code applies it for everyone, so tell the team before you add a migration.
To undo a migration you have not pushed or applied yet: `dotnet ef migrations remove`.
Two people adding migrations at the same time can conflict.

## What changed so far
Items 1 to 7 are merged into `main` (pull request #1). Item 8 is on the branch `claude/keen-davinci-s4eek9` and is not merged yet.

1. **Null fix**: reading a broken .docx no longer risks a `NullReferenceException` (`BooksController.ExtractText`).
2. **Manage books** for admins: add, edit and delete books, with cover and PDF uploads (`AdminBooksController`, `BookForm`, `Views/AdminBooks`).
3. **Admin login moved to configuration**: the email and password are no longer in `SeedData.cs` (see Accounts).
4. **Library search**: search box and nationality filter on the home page.
5. **EF Core migrations** replace `EnsureCreated`: model changes no longer require deleting `app.db`, and databases from before migrations are kept.
6. **Translation review fixes**: decide once, one published translation per book (older ones become Replaced), rejection reasons, the My translations page, and one pending translation per user per book. Migration `TranslationReview` adds `SubmittedByUserId`, `DecidedAt` and `RejectionReason`.
7. `.gitignore` now also ignores `wwwroot/uploads/`, `.vs/` and the SQLite side files.
8. **Database switched from SQLite to PostgreSQL (Supabase)**: new `DatabaseConfig` reads `ConnectionStrings:Default` (Npgsql or `postgresql://` form, encryption on for remote hosts), the SQLite package and the SQLite-only upgrade code are gone, and the migrations were regenerated for Postgres as a single `InitialCreate`. Data in an old local `app.db` is not carried over.
9. **Row-level security on every table**, applied automatically at startup (see Notes), so Supabase's public API key cannot read or change the app's tables.
10. **Container and hosting setup for Render**: `Dockerfile`, the app listens on the host's `PORT`, and trusts the host's HTTPS proxy headers (see Hosting on Render).
11. **Login keys stored in the database** (`DataProtectionKeys`, migration `DataProtectionKeys`) so logins survive redeploys.
12. **Uploads in Supabase Storage** (`Services/FileStorage.cs`): covers and PDFs survive redeploys; the bucket is created automatically; the local folder is still used when no storage is configured.
13. **Book details**: year, original language and a hidden flag (migration `BookDetails`, which also fills these in for the 8 sample books).
14. **Users page** for admins: make and remove admins, block and unblock, password reset links.
15. **Account safety**: lockout after 5 wrong passwords, quicker sign-out of blocked users, and an honest forgot-password page.
16. **Automated tests** and a GitHub workflow that runs them on every pull request.
17. **Email through Resend** (`Services/Email.cs`): email confirmation for new accounts, password reset emails, rate limiting on the email pages, and a "Confirm email" button for admins. Off until `Email:Resend:ApiKey` and `Email:From` are set.

## Not done yet
- A domain for email: until one is verified in Resend, emails only reach the Resend account's own address.
- Google login is untested.
- The first commit in git history still contains the old hardcoded admin password. Change that password anywhere it was used.
