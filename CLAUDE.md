# CLAUDE.md

Guidance for Claude Code sessions in this repository. Read README.md for full setup details.

## Project
Book Library: a site for reading books and for submitting Bulgarian translations of books that have none.
Users submit translations (.docx or text-based PDF); admins approve or reject them and manage books.
Live at https://book-library-jsmg.onrender.com (Render service `book-library`, auto-deploys from `main`).

## Stack and layout
- ASP.NET Core MVC on .NET 10 (`net10.0`), ASP.NET Identity, EF Core with PostgreSQL (Npgsql) on Supabase.
- `Controllers/`: `HomeController` (landing page `Index` at `/`, library and search `Library` at `/Library`, the 3D bookshelf page `Shelf`), `BooksController` (book page, submit, My translations),
  `AdminController` (translation review), `AdminBooksController` (book management), `AdminUsersController` (users, admin role, blocking, reset links).
- `Models/`: `Book` (with `Year`, `Language`, `Hidden`: hidden books are admin-only everywhere), `Translation`, `TranslationStatus` (Pending, Approved, Rejected, Replaced: only add new values at the end), `BookForm`.
- `Data/`: `ApplicationDbContext`, `DatabaseConfig` (connection string parsing), `SeedData` (migrations, row-level security, admin, sample books), `Migrations/`.
- `Services/FileStorage.cs`: admin uploads, Supabase Storage or the local `wwwroot/uploads/` folder.
- `Services/LockoutSignInManager.cs`: makes the login count wrong passwords (lockout after 5). `Areas/Identity/Pages/Account/ForgotPassword.cshtml` replaces the built-in page.
- `tests/BookLibrary.Tests/`: xUnit tests; `AppFactory` starts the app on a throwaway database, `Browser` fills in forms with their anti-forgery token.
- `Dockerfile`: the image Render builds.
- Landing page and dark look: `Views/Home/Index.cshtml` + `wwwroot/landing/`, shared header `Views/Shared/_VibeHeader.cshtml`; a page using the site layout turns on the dark look with `ViewData["Vibe"] = true` (`wwwroot/css/vibe.css`, scoped to `body.vibe`).

## Run and check
- Build: `dotnet build` (keep it at 0 warnings).
- Run: `dotnet run`, then open https://localhost:7080 by hand. Needs the .NET 10 runtime (a newer major version alone will not run it).
- Tests: `dotnet test tests/BookLibrary.Tests`. They need PostgreSQL on localhost:5432 (user and password `postgres`) or `TEST_DATABASE` set to a server connection string. GitHub runs them on every pull request (`.github/workflows/tests.yml`).
- Add or update tests with every change, and keep them all passing. Also try the affected pages by hand when the change is visual.
- Settings come from user secrets locally and environment variables on Render (double underscores there):
  `ConnectionStrings:Default`, `Admin:Email`, `Admin:Password`, `Storage:Supabase:Url`, `Storage:Supabase:Key`, optional `Storage:Supabase:Bucket`,
  `Email:Resend:ApiKey`, `Email:From`, optional `Email:RequestsPer15Minutes`.
- Email (`Services/Email.cs`) goes through Resend's web API, never SMTP (Render's free plan blocks SMTP ports). With the email settings present, new accounts must confirm their email and "Forgot password" sends a link; without them nothing is sent. Settings that change behaviour are read lazily (options, per request) so the tests can switch them with `UseSetting`.

## Rules
- Never put passwords, connection strings or keys in the repo, in commits, in logs, or in chat. Use placeholders in examples. If the user pastes one, tell them to reset it.
- Commits are authored as the user: `bohoSOL <332903396+bohoSOL@users.noreply.github.com>`. Do not add Co-Authored-By, "Generated with Claude" or session lines to commits or pull requests.
- Work on a branch and open a pull request into `main`. Never push to `main` directly, never force-push `main`, and never merge a pull request: the team reviews and merges.
- Database changes: change the model, then `dotnet tool restore` and `dotnet ef migrations add <Name> --output-dir Data/Migrations`, and commit the migration with the change. The team shares one Supabase database and migrations run on app start, so tell the user before adding one.
- The app must connect as the table owner (Supabase's `postgres.<ref>` user, Session pooler on port 5432, not the Transaction pooler on 6543). Row-level security is turned on for every public table at startup; do not remove that.
- Seeded files in `wwwroot/covers` and `wwwroot/pdfs` are part of the repo; only delete files the app uploaded.
- Keep the README up to date when behaviour or setup changes.

## Working with the user
- The user works on Windows (PowerShell, Visual Studio) and a Mac (Terminal, VS Code). Give commands ready to copy, one per block, and say which terminal they are for. In PowerShell, connection strings go in single quotes.
- Explain things simply and say clearly what the user has to do themselves (for example settings in the Render or Supabase dashboards).
