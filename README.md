# Book Library (demo)

ASP.NET Core MVC + Identity + SQLite.


A site for reading books and for sending in Bulgarian translations of books that have none. Visitors browse and search the library. Logged-in users submit translations as .docx or PDF files. An admin reviews each one and approves or rejects it, and approved translations are published on the book page. Admins also add, edit and delete books.

## Run
    dotnet run
Open https://localhost:7080 (first run creates `app.db` and seeds 8 books).
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
- Book page: summary, **Read in English** (PDF, shown only if the book has one), and **Read in Bulgarian** once a translation is approved.

**Logged-in users**
- **Translate to Bulgarian**: upload a .docx or a text-based PDF.
- One translation per book can wait for review at a time. While it waits, the book page shows its status instead of the Translate button.
- **My translations** (top menu): every submission with its status: waiting for review, published, rejected (with the admin's reason) or replaced by a newer translation. After a rejection, **Send a new version**.

**Admins** (menu items appear only for admins)
- **Translation requests**: read a submission and approve or reject it, with an optional reason for rejecting.
  - A translation is decided only once; deciding it again (double click, second tab, back button) changes nothing.
  - Only one approved translation per book. Approving a new one replaces the old one, and the review page warns before doing so.
- **Manage books**: add, edit and delete books.
  - Fields: title, author, nationality (suggests existing ones), summary.
  - Optional cover image (.png, .jpg, .webp) and English PDF. SVG covers are not accepted because they can contain scripts.
  - Uploads are saved in `wwwroot/uploads/` under random names. PDFs must start with the `%PDF` header. Replaced or deleted uploads are removed from disk. The seeded files in `wwwroot/covers` and `wwwroot/pdfs` are never deleted.
  - Deleting a book also deletes its translations; the confirmation page says how many.

## Flow to try
1. Home: click a nationality to expand it and see covers, click a book. Use the search box to find a book by title or author, or filter by nationality.
2. A book with no Bulgarian translation shows **Read in English** (opens the PDF) and **Translate to Bulgarian**.
3. Log in as a normal user, press Translate, upload `test-files/sample-bulgarian-translation.docx`.
4. Log in as admin, open **Translation requests**, read the text, Approve or Reject (optionally with a reason).
5. After approval the book shows **Read in Bulgarian**. Approving another translation of the same book replaces it.
6. The translator follows their submissions on **My translations**: waiting, published, rejected (with the reason) or replaced. After a rejection they can send a new version.
7. As admin, open **Manage books** and add a book with a cover and a PDF, edit it, then delete it.

## Project layout
- `Controllers/`: `HomeController` (library and search), `BooksController` (book page, submit translation, My translations), `AdminController` (translation review), `AdminBooksController` (book management)
- `Models/`: `Book`, `Translation` and `TranslationStatus` (Pending, Approved, Rejected, Replaced), `BookForm` (admin add/edit form)
- `Data/`: `ApplicationDbContext`, `SeedData` (migrations, sample books, admin account), `Migrations/`
- `Views/`: Razor pages for each controller
- `wwwroot/`: seeded covers and PDFs, plus `uploads/` (created at run time, not in git)
- `test-files/`: a sample .docx translation

## Notes
- English PDFs in `wwwroot/pdfs` are placeholders; replace each with the real file (same name).
- Translation uploads must be .docx or a text-based PDF (scans cannot be read). A .docx with no readable body is treated as unreadable instead of crashing.
- Files in `wwwroot/uploads/` and `app.db` stay on your own PC; they are not in git. A server needs somewhere permanent for them.
- The database is updated with EF Core migrations (in `Data/Migrations`). The app applies any new ones when it starts, so you never need to delete `app.db`. A database made before migrations were added is detected and kept as it is.

## Changing the data model
After changing a model (for example adding a field to `Book`), create a migration and commit it with your change:

    dotnet tool restore
    dotnet ef migrations add AddBookYear --output-dir Data/Migrations

Use a name that says what changed. `dotnet tool restore` is only needed once per PC.
Start the app and the change is applied to your `app.db`; teammates get it the same way after they pull.
To undo a migration you have not pushed yet: `dotnet ef migrations remove`.
Two people adding migrations at the same time can conflict, so tell the team before you add one.

## What changed so far
Work is on the branch `claude/keen-davinci-s4eek9` and has not been merged into `main`.

1. **Null fix**: reading a broken .docx no longer risks a `NullReferenceException` (`BooksController.ExtractText`).
2. **Manage books** for admins: add, edit and delete books, with cover and PDF uploads (`AdminBooksController`, `BookForm`, `Views/AdminBooks`).
3. **Admin login moved to configuration**: the email and password are no longer in `SeedData.cs` (see Accounts).
4. **Library search**: search box and nationality filter on the home page.
5. **EF Core migrations** replace `EnsureCreated`: model changes no longer require deleting `app.db`, and databases from before migrations are kept.
6. **Translation review fixes**: decide once, one published translation per book (older ones become Replaced), rejection reasons, the My translations page, and one pending translation per user per book. Migration `TranslationReview` adds `SubmittedByUserId`, `DecidedAt` and `RejectionReason`.
7. `.gitignore` now also ignores `wwwroot/uploads/`, `.vs/` and the SQLite side files.

## Not done yet
- Automated tests (everything so far was checked by hand in a browser).
- More book details such as year, language or a hidden/published flag. Decide with the team first.
- Hosting and permanent storage for uploads.
- Email confirmation, password reset by email, and a page for admins to manage users.
- Google login is untested.
- The first commit in git history still contains the old hardcoded admin password. Change that password anywhere it was used.
