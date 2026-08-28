# Déjà Dans la Bibli (BookCatalog)

A Progressive Web App for a family to catalogue the physical books it owns,
organised into custom **collections** (e.g. "Kids' books", "Greg's books",
"Fa's books"). It is an inventory of what is on the shelves — there is no
wishlist or reservation feature.

The app is a pure client: Blazor WebAssembly talking straight to Supabase
(Postgres + Auth + Storage) from the browser. There is no .NET server.

> The repository also contains [`BookCatalog/README.md`](BookCatalog/README.md)
> (in French), which is the step-by-step Supabase / deployment setup guide.
> This file is the up-to-date functional and technical overview.

---

## Features

### Authentication & roles
- Email/password sign-up and sign-in (Supabase Auth), "forgotten password"
  flow with an emailed reset link handled by `Pages/ResetPassword.razor`.
- Session persisted in `localStorage` and restored on startup.
- Three roles, enforced by Postgres Row Level Security (client-side checks are
  only for hiding UI):
  - **readonly** — can browse collections, books, labels and search; no edits.
  - **admin** — full CRUD on collections, books and labels, plus user
    management.
  - **superadmin** — same as admin, but a superadmin profile can't be removed
    from the Users screen and only another superadmin can change its role.
- A Postgres trigger auto-creates a `profiles` row (role `readonly`) on every
  sign-up; the first user is promoted manually via SQL.

### Home
- Tile launcher (`Pages/Home.razor`): Search, Collections, and — for admins —
  Users and Labels.

### Collections
- List, create, rename, delete (admin only). A book belongs to exactly one
  collection; deleting a collection cascades to its books and their photos.
- The collections list has a cross-collection ISBN quick-search box.

### Books
- Fields: 1–2 photos, title, author, optional ISBN, collection, and any number
  of free-form labels.
- **ISBN lookup** (`IsbnLookupService`): entering an ISBN and hitting "Rechercher"
  pre-fills title, author and a cover image by merging results from the BnF SRU
  catalogue, Open Library and Google Books, with an Open Library cover fallback.
- **Camera barcode scan**: the ISBN field (in the book form and in Search) has a
  "Scanner" button that opens the camera and reads an ISBN barcode via ZXing
  (`wwwroot/js/barcodeScanner.js`, bundled `zxing-0.21.3`).
- **Image handling**: photos are cropped in-browser (`imageCropper.js`),
  compressed to ~1200px / JPEG ~80% via `<canvas>` (`imageTools.js`) *before*
  upload — raw photos are never sent. Files land in the public Supabase Storage
  bucket `book-photos/{collection_id}/{book_id}/`; only the public URL is stored
  in `books.photo_url_1/2`.

### Collection detail view
- Two view modes, user-togglable: **list** (thumbnail — title — author) and
  **tile** (cover grid, title only).
- Collapsible filter panel: by title, by author, by label (with autocomplete),
  plus a sort selector.
- Tap any cover to open it in a full-screen image viewer
  (`Shared/FullscreenImage.razor`).

### Search (`/recherche`)
- Cross-collection search in three modes: **ISBN**, **Title**, **Author**.
- ISBN mode also supports the camera scanner and, when nothing matches, lists
  books that have no ISBN recorded so you can check by eye.
- Results link back to the owning collection and show its name.

### Labels
- Shared, case-insensitive tags with a many-to-many link to books
  (`book_labels`). Added from the book form.
- Admin page (`/labels`) to rename or delete a label across every book that
  carries it.

### Theme
- Light/dark palette driven by CSS custom properties. By default it follows the
  device (`prefers-color-scheme`); a toggle in the top bar and on the login
  screen (**Système / Clair / Sombre**) lets the user pin a choice, stored in
  `localStorage` and applied before first paint to avoid a flash.

### PWA
- Installable ("Add to Home Screen" / install icon in the address bar).
- Manifest + service worker from the Blazor WASM PWA template; the service
  worker is only active in a published/Release build. An in-app banner prompts
  a reload when a new version is available.
- UI language is French.

---

## Tech stack

| Layer        | Choice                                                            |
|--------------|-----------------------------------------------------------------|
| Frontend     | Blazor WebAssembly, .NET 8, PWA template                        |
| Backend      | Supabase — Postgres, Auth, Storage, auto REST API              |
| Supabase SDK | `Supabase` NuGet package `1.1.1` (called from the browser)     |
| Auth state   | `CustomAuthStateProvider` + `Microsoft.AspNetCore.Components.Authorization` |
| Image ops    | Plain JS + `<canvas>` interop (no .NET imaging lib)            |
| Barcode      | ZXing (`zxing-0.21.3.min.js`)                                  |
| ISBN data    | BnF SRU, Open Library, Google Books                            |
| Hosting      | GitHub Pages (`.github/workflows/deploy.yml`); Azure Static Web Apps documented as an alternative |
| Keep-alive   | `.github/workflows/keepalive.yml` pings Supabase every 3 days to stop the free project pausing |

Supabase URL and anon/publishable key live in
[`BookCatalog/wwwroot/appsettings.json`](BookCatalog/wwwroot/appsettings.json).
The anon key is public by design; RLS on the server is the real barrier.

---

## Project structure

```
BookCatalog/
  Pages/
    Home.razor                Login.razor            ResetPassword.razor
    Collections.razor          CollectionDetail.razor  BookForm.razor
    Search.razor               Labels.razor            Users.razor   NotFound.razor
  Layout/
    MainLayout.razor  NavMenu.razor  LoginLayout.razor  RedirectToLogin.razor
  Shared/
    BookListItem.razor  BookTile.razor  FullscreenImage.razor
    IsbnScanSearch.razor  ThemeToggle.razor
  Services/
    SupabaseService              client wrapper (singleton, DI)
    AuthService                  login/logout, current profile, IsAdmin/IsSuperAdmin
    CustomAuthStateProvider      bridges Supabase session to Blazor auth
    LocalStorageSessionPersistence   session <-> localStorage (sync JS interop)
    CollectionService  BookService  LabelService  UserService
    IsbnLookupService            BnF / Open Library / Google Books merge
    ImageUploadService           compress + upload + delete
    ImageViewerService           full-screen viewer state
  Models/
    Book.cs  BookCollection.cs  Label.cs  BookLabel.cs  Profile.cs
  wwwroot/
    css/app.css   index.html   appsettings.json   manifest.webmanifest
    js/  authRedirect.js  imageTools.js  imageCropper.js  barcodeScanner.js
         vendor/zxing-0.21.3.min.js
    service-worker.js  service-worker.published.js
  sql/schema.sql                tables + RLS + trigger + storage bucket
```

---

## Data model

```
profiles
  id (uuid, FK auth.users)  display_name  role (superadmin|admin|readonly)  created_at

collections
  id (uuid, PK)  name  created_at  created_by (FK profiles)

books
  id (uuid, PK)  title  author  isbn (nullable)
  collection_id (FK collections, on delete cascade)
  photo_url_1 (nullable)  photo_url_2 (nullable)
  created_at  created_by (FK profiles)

labels
  id (uuid, PK)  name  created_at        -- unique on lower(name)

book_labels                              -- many-to-many
  id (uuid, PK)  book_id (FK books)  label_id (FK labels)  created_at
  unique (book_id, label_id)
```

RLS: any user with a profile can `SELECT` collections / books / labels;
`INSERT/UPDATE/DELETE` on those tables and on Storage objects require
`is_admin()`. `profiles` rows are readable by their owner and by admins;
superadmin rows are protected as described above. Full script and policies:
[`BookCatalog/sql/schema.sql`](BookCatalog/sql/schema.sql).

---

## Running locally

```bash
cd BookCatalog
dotnet restore
dotnet run
```

Open the printed URL (e.g. `https://localhost:7xxx`). To exercise the PWA /
service worker, publish first (`dotnet publish -c Release`) and serve the static
output — the service worker is inactive in Debug.

First-time Supabase setup (run `sql/schema.sql`, configure Auth redirect URLs,
promote the first user, GitHub Pages deployment, keep-alive secrets) is covered
step by step in [`BookCatalog/README.md`](BookCatalog/README.md).

---

## Known limitations

- **Deleting an account** — the Users page can only revoke access (delete the
  `profiles` row). Fully deleting an `auth.users` account needs the
  `service_role` key, which must never ship in a WASM client; do it from the
  Supabase dashboard.
- **Public Storage bucket** — `book-photos` is public so plain public URLs can
  be stored and rendered directly. Write/delete stay admin-only via RLS, but
  Supabase's public-read route bypasses RLS by design (acceptable for book
  covers in a family app).

## Out of scope (v2)

Wishlist / reservations, cover recognition by vision/OCR, notifications,
sharing and export.
