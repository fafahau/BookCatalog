-- BookCatalog — schema, RLS policies, auto-profile trigger, storage bucket + policies.
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this whole file → Run.
-- Safe to re-run: every statement is idempotent (IF NOT EXISTS / OR REPLACE / ON CONFLICT / DROP ... IF EXISTS).

-- ============================================================
-- 1. Tables
-- ============================================================

create table if not exists public.profiles (
    id           uuid primary key references auth.users (id) on delete cascade,
    display_name text,
    role         text not null default 'readonly' check (role in ('superadmin', 'admin', 'readonly')),
    created_at   timestamptz not null default now()
);

create table if not exists public.collections (
    id         uuid primary key default gen_random_uuid(),
    name       text not null,
    created_at timestamptz not null default now(),
    created_by uuid references public.profiles (id) on delete set null
);

create table if not exists public.books (
    id            uuid primary key default gen_random_uuid(),
    title         text not null,
    author        text not null,
    isbn          text,
    collection_id uuid not null references public.collections (id) on delete cascade,
    photo_url_1   text,
    photo_url_2   text,
    created_at    timestamptz not null default now(),
    updated_at    timestamptz not null default now(),
    created_by    uuid references public.profiles (id) on delete set null
);

create index if not exists books_collection_id_idx on public.books (collection_id);

-- updated_at: last time the book changed — a direct edit to the row, or a label
-- linked / unlinked (see section 2b). Added after the initial release, so bring
-- it in nullable, backfill existing rows from created_at, then lock it down.
alter table public.books add column if not exists updated_at timestamptz;
update public.books set updated_at = created_at where updated_at is null;
alter table public.books alter column updated_at set default now();
alter table public.books alter column updated_at set not null;

-- Widen the role check to allow 'superadmin' when re-running against an existing DB
-- (create table if not exists above leaves the old constraint in place).
alter table public.profiles drop constraint if exists profiles_role_check;
alter table public.profiles
    add constraint profiles_role_check check (role in ('superadmin', 'admin', 'readonly'));

-- Free-form labels/tags (added after the initial release). A label is a shared
-- row; books and labels have a many-to-many link via book_labels (each book
-- carries 0-N labels).
create table if not exists public.labels (
    id         uuid primary key default gen_random_uuid(),
    name       text not null,
    created_at timestamptz not null default now()
);

-- Case-insensitive uniqueness: "Jeunesse" and "jeunesse" are the same label.
create unique index if not exists labels_name_lower_key on public.labels (lower(name));

create table if not exists public.book_labels (
    id         uuid primary key default gen_random_uuid(),
    book_id    uuid not null references public.books (id) on delete cascade,
    label_id   uuid not null references public.labels (id) on delete cascade,
    created_at timestamptz not null default now(),
    unique (book_id, label_id)
);

create index if not exists book_labels_book_id_idx on public.book_labels (book_id);
create index if not exists book_labels_label_id_idx on public.book_labels (label_id);

-- ============================================================
-- 2. Auto-create a profile row whenever a new auth user signs up
--    (default role 'readonly' — an admin promotes manually afterwards).
-- ============================================================

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    insert into public.profiles (id, display_name, role)
    values (new.id, new.raw_user_meta_data ->> 'display_name', 'readonly');
    return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert on auth.users
    for each row execute procedure public.handle_new_user();

-- ============================================================
-- 2b. Keep books.updated_at fresh:
--     - a direct edit to a book row               -> BEFORE UPDATE on books
--     - a label linked to / unlinked from a book  -> AFTER INSERT/DELETE on book_labels
-- ============================================================

create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists books_set_updated_at on public.books;
create trigger books_set_updated_at
    before update on public.books
    for each row execute procedure public.set_updated_at();

create or replace function public.touch_book_updated_at()
returns trigger
language plpgsql
as $$
begin
    update public.books
        set updated_at = now()
        where id = coalesce(new.book_id, old.book_id);
    return coalesce(new, old);
end;
$$;

drop trigger if exists book_labels_touch_book on public.book_labels;
create trigger book_labels_touch_book
    after insert or delete on public.book_labels
    for each row execute procedure public.touch_book_updated_at();

-- ============================================================
-- 3. Helpers: role checks for the current authenticated user.
--    SECURITY DEFINER so they can read profiles regardless of the caller's
--    own RLS visibility, avoiding any recursive-policy edge cases.
--    'superadmin' is a strict superset of 'admin': is_admin() is true for both,
--    so every admin-only policy below also covers superadmins.
-- ============================================================

create or replace function public.is_admin()
returns boolean
language sql
security definer
set search_path = public
stable
as $$
    select exists (
        select 1 from public.profiles
        where id = auth.uid() and role in ('admin', 'superadmin')
    );
$$;

create or replace function public.is_superadmin()
returns boolean
language sql
security definer
set search_path = public
stable
as $$
    select exists (
        select 1 from public.profiles
        where id = auth.uid() and role = 'superadmin'
    );
$$;

-- ============================================================
-- 4. Row Level Security (brief 4.5)
-- ============================================================

alter table public.profiles enable row level security;
alter table public.collections enable row level security;
alter table public.books enable row level security;
alter table public.labels enable row level security;
alter table public.book_labels enable row level security;

-- profiles: everyone can read their own row (needed to know their own role);
-- admins can read/update/delete every row (Users.razor), EXCEPT that a
-- 'superadmin' row can only be modified by another superadmin and can never
-- be deleted through the app.
drop policy if exists profiles_select on public.profiles;
create policy profiles_select on public.profiles
    for select using (id = auth.uid() or public.is_admin());

drop policy if exists profiles_admin_update on public.profiles;
create policy profiles_admin_update on public.profiles
    for update
    using (public.is_admin() and (role <> 'superadmin' or public.is_superadmin()))
    with check (public.is_admin() and (role <> 'superadmin' or public.is_superadmin()));

drop policy if exists profiles_admin_delete on public.profiles;
create policy profiles_admin_delete on public.profiles
    for delete using (public.is_admin() and role <> 'superadmin');

-- collections: any signed-in user with a profile can read;
-- only admins can create/rename/delete.
drop policy if exists collections_select on public.collections;
create policy collections_select on public.collections
    for select using (exists (select 1 from public.profiles where id = auth.uid()));

drop policy if exists collections_admin_write on public.collections;
create policy collections_admin_write on public.collections
    for all using (public.is_admin()) with check (public.is_admin());

-- books: same shape as collections.
drop policy if exists books_select on public.books;
create policy books_select on public.books
    for select using (exists (select 1 from public.profiles where id = auth.uid()));

drop policy if exists books_admin_write on public.books;
create policy books_admin_write on public.books
    for all using (public.is_admin()) with check (public.is_admin());

-- labels / book_labels: same shape as books — any profile reads, admins write.
drop policy if exists labels_select on public.labels;
create policy labels_select on public.labels
    for select using (exists (select 1 from public.profiles where id = auth.uid()));

drop policy if exists labels_admin_write on public.labels;
create policy labels_admin_write on public.labels
    for all using (public.is_admin()) with check (public.is_admin());

drop policy if exists book_labels_select on public.book_labels;
create policy book_labels_select on public.book_labels
    for select using (exists (select 1 from public.profiles where id = auth.uid()));

drop policy if exists book_labels_admin_write on public.book_labels;
create policy book_labels_admin_write on public.book_labels
    for all using (public.is_admin()) with check (public.is_admin());

-- ============================================================
-- 5. Storage bucket + policies (brief 4.2 / 4.3 / 4.5)
--
-- The bucket is public so photo_url_1/2 can be plain public URLs rendered
-- directly in <img> tags (brief 4.2 explicitly stores "l'URL publique").
-- Note: Supabase's public-read route bypasses RLS by design - the SELECT
-- policy below only matters for authenticated listing via the REST API.
-- INSERT/UPDATE/DELETE always go through RLS regardless of the public flag,
-- so uploads/deletes stay admin-only either way.
-- ============================================================

insert into storage.buckets (id, name, public)
values ('book-photos', 'book-photos', true)
on conflict (id) do update set public = true;

drop policy if exists book_photos_select on storage.objects;
create policy book_photos_select on storage.objects
    for select using (
        bucket_id = 'book-photos'
        and exists (select 1 from public.profiles where id = auth.uid())
    );

drop policy if exists book_photos_admin_write on storage.objects;
create policy book_photos_admin_write on storage.objects
    for all using (bucket_id = 'book-photos' and public.is_admin())
    with check (bucket_id = 'book-photos' and public.is_admin());

-- ============================================================
-- 6. Bootstrap: promote the first user to admin (or superadmin) manually, e.g.:
--   update public.profiles set role = 'superadmin' where id = '<uuid from auth.users>';
--
-- A superadmin has every admin capability plus: their profile cannot be deleted
-- from the Users screen and their role can only be changed by another superadmin.
-- To remove or demote the last superadmin, run SQL from the Supabase dashboard.
-- ============================================================
