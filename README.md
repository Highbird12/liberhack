# Book Library (demo)

ASP.NET Core MVC + Identity + SQLite.
tova e sait za chetene na BG sknigi i davane na submisions na bg prevodi na ne prevedeni knigi, vse oshte ne sum go testval. pitaj ai za oshte info kum coda ko shto

## Run
    dotnet run
Open https://localhost:7080 (first run creates `app.db` and seeds 8 books).
Targets .NET 10; for an older SDK change `TargetFramework` and the `10.0.*` package versions in BookLibrary.csproj.

## Accounts
- Register / Log in: the Identity pages under the top-right menu.
- The admin account is not in the code. Each person sets their own once, with user secrets (stored on your PC, outside the repo):

      dotnet user-secrets set "Admin:Email" "you@example.com"
      dotnet user-secrets set "Admin:Password" "YourPassword1!"

  The password needs upper and lower case, a digit and a symbol. Restart the app and the account is created.
  If the email belongs to an account that already exists, that account becomes admin (its password is not changed).
- On a server, set the environment variables `Admin__Email` and `Admin__Password` instead.
- Without these settings the app still runs, but no admin is created (a warning is logged at startup).

## Google login (optional)
1. Google Cloud Console > APIs & Services > Credentials > Create OAuth client ID (Web application).
2. Authorized redirect URI: `https://localhost:7080/signin-google`
3. Then:
       dotnet user-secrets set "Google:ClientId" "YOUR_ID"
       dotnet user-secrets set "Google:ClientSecret" "YOUR_SECRET"
4. Restart. A "Google" button appears on the Log in and Register pages. Without keys it stays hidden.

## Flow to try
1. Home: click a nationality to expand it and see covers, click a book. Use the search box to find a book by title or author, or filter by nationality.
2. A book with no Bulgarian translation shows **Read in English** (opens the PDF) and **Translate to Bulgarian**.
3. Log in as a normal user, press Translate, upload `test-files/sample-bulgarian-translation.docx`.
4. Log in as admin, open **Translation requests**, read the text, Approve or Reject.
5. After approval the book shows **Read in Bulgarian**.

## Notes
- English PDFs in `wwwroot/pdfs` are placeholders; replace each with the real file (same name).
- Uploads must be .docx or a text-based PDF (scans cannot be read).
- If you change the models, delete `app.db` so it is recreated.
