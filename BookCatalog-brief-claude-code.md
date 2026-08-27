# BookCatalog — Cahier des charges technique

## 1. Objectif du projet

Application web/PWA permettant à une famille de cataloguer les livres physiques qu'elle possède, organisés en **collections** personnalisées (ex. "Livres des enfants", "Livres de Greg", "Livres de Fa"). Pas de gestion de liste de souhaits ni de réservation — uniquement l'inventaire de ce qui est possédé.

## 2. Stack technique imposée

- **Frontend** : Blazor WebAssembly (.NET 8), configuré en PWA (template "Progressive Web Application" activé)
- **Backend** : Supabase (Postgres, Auth, Storage, API REST auto-générée)
- **Client Supabase C#** : package NuGet `Supabase` (client communautaire, repo `supabase-community/supabase-csharp`)
- **Hébergement cible** : Azure Static Web Apps ou GitHub Pages (fichiers statiques uniquement, avec SPA fallback pour le routing Blazor)
- **CI/CD** : GitHub Actions

Pas de backend .NET serveur — tout passe par l'API Supabase appelée depuis le navigateur (WASM).

## 3. Fonctionnalités attendues

### 3.1 Collections
- CRUD complet (créer, lister, renommer, supprimer) — réservé aux admins
- Le nom d'une collection doit être éditable après création
- Un livre appartient à exactement une collection

### 3.2 Livres
Un livre est caractérisé par :
- 1 ou 2 photos
- Titre
- Auteur
- ISBN (optionnel — certains livres n'en ont pas)
- Collection d'appartenance

CRUD complet réservé aux admins. Les read-only ne peuvent que consulter.

### 3.3 Saisie avec ou sans ISBN
- Si l'utilisateur saisit un ISBN → appeler une API externe gratuite (**Open Library API** ou **Google Books API**) pour pré-remplir automatiquement titre, auteur, et éventuellement une image de couverture
- Si pas d'ISBN → saisie 100% manuelle de tous les champs

### 3.4 Affichage de la liste de livres
Deux modes d'affichage, togglable par l'utilisateur :
- **Vue liste** : image miniature — titre — auteur
- **Vue tuile** : image — titre uniquement (grille)

### 3.5 Filtres
La liste de livres doit être filtrable par :
- Nom (titre)
- Auteur
- Date de création (ajout au catalogue)

### 3.6 Rôles utilisateurs
- **Admin** : gestion des livres (ajout/suppression/édition) + gestion des autres utilisateurs (changer leur rôle, les retirer)
- **Read-only** : consultation uniquement, aucune action de modification visible ni autorisée

### 3.7 Design
- Responsive, priorité à l'usage smartphone (mobile-first)
- Fond blanc
- Pas de contrainte de charte graphique précise au-delà de ça — rester simple et lisible

### 3.8 PWA
- Installable sur smartphone ("Ajouter à l'écran d'accueil")
- Manifest + service worker générés via le template Blazor WASM PWA

## 4. Points techniques spécifiques à respecter absolument

### 4.1 Compression d'image obligatoire avant upload
**Ne jamais uploader une photo brute.** Avant tout envoi vers Supabase Storage :
- Redimensionner à ~1200px de large maximum
- Réencoder en JPEG qualité ~80%
- Objectif : ~100-250 KB par photo (au lieu de 3-8 MB en brut)
- Raison : le plan gratuit Supabase n'offre que 1 GB de stockage fichiers ; sans compression, ce quota serait atteint après quelques dizaines de livres seulement. Avec compression, on tient plusieurs milliers de photos.
- Implémentation suggérée : JS interop avec `<canvas>` (`canvas.toBlob` avec qualité réglable) plutôt qu'une lib .NET d'imaging (plus légère à charger en WASM)
- Afficher un aperçu de l'image compressée avant validation du formulaire

### 4.2 Stockage : fichier binaire + URL, jamais de Base64 en base
- Les photos vont dans **Supabase Storage** (bucket dédié), pas en Base64 dans une colonne Postgres
- La table `books` ne stocke que l'**URL publique** de chaque photo (texte court)
- Raisons : Base64 augmente la taille de ~33% par rapport au binaire ; ça alourdirait la base de données (limitée à 500 MB sur le plan gratuit, contre 1 GB pour le Storage) ; ça empêche la mise en cache navigateur ; ça ralentit les `SELECT` sur la table `books`

### 4.3 Organisation du bucket Storage
Convention de nommage pour faciliter la suppression en cascade :
```
book-photos/
  {collection_id}/
    {book_id}/
      photo1.jpg
      photo2.jpg
```
Quand un livre est supprimé, supprimer aussi ses fichiers associés dans le bucket.

### 4.4 Empêcher la pause automatique du projet Supabase gratuit
Le plan gratuit Supabase met en pause tout projet inactif pendant 7 jours (aucune requête reçue). Comme cette app ne sera pas utilisée quotidiennement, il faut prévoir dès le départ :
- Un **workflow GitHub Actions** planifié (cron, ex. tous les 3 jours) qui exécute une requête légère sur la base (ex. `SELECT count(*) FROM collections`) via l'API REST Supabase, pour réinitialiser le compteur d'inactivité
- URL Supabase + clé anon stockées en secrets GitHub Actions (jamais en clair dans le repo)
- Documenter dans le README la procédure de restauration manuelle (dashboard Supabase → Restore) au cas où le ping échoue

### 4.5 Sécurité / RLS (Row Level Security)
- Toutes les tables (`profiles`, `collections`, `books`) doivent avoir RLS activé
- `readonly` : `SELECT` uniquement sur `collections` et `books`
- `admin` : `SELECT/INSERT/UPDATE/DELETE` sur `collections` et `books`, et droit de modifier `profiles` (rôle des autres utilisateurs)
- Le bucket Storage `book-photos` doit avoir ses propres policies : lecture pour tout utilisateur authentifié, écriture/suppression réservées aux admins
- Ne jamais faire confiance à un contrôle uniquement côté client (masquer un bouton n'est pas une sécurité) — la RLS côté Supabase est la vraie barrière

## 5. Modèle de données (base pour le schéma SQL)

```
profiles
  id (uuid, FK vers auth.users)
  display_name (text)
  role (text: 'admin' | 'readonly')
  created_at (timestamptz)

collections
  id (uuid, PK)
  name (text, éditable)
  created_at (timestamptz)
  created_by (uuid, FK vers profiles)

books
  id (uuid, PK)
  title (text)
  author (text)
  isbn (text, nullable)
  collection_id (uuid, FK vers collections)
  photo_url_1 (text, nullable)
  photo_url_2 (text, nullable)
  created_at (timestamptz)
  created_by (uuid, FK vers profiles)
```

Un trigger Postgres sur `auth.users` (after insert) doit créer automatiquement une ligne `profiles` correspondante, avec un rôle par défaut (probablement `readonly` par sécurité — l'admin promeut ensuite manuellement).

## 6. Structure applicative suggérée

```
Pages/
  Login.razor
  Collections.razor              → liste des collections (accueil après login)
  CollectionDetail.razor         → /collection/{id} — liste des livres, toggle liste/tuile, filtres
  BookForm.razor                 → /collection/{id}/book/new et /book/{id}/edit
  Users.razor                    → gestion des utilisateurs (admin only)

Shared/
  MainLayout.razor
  NavMenu.razor
  BookListItem.razor             → composant vue liste
  BookTile.razor                 → composant vue tuile

Services/
  SupabaseService                → wrapper du client Supabase.Client (singleton, DI)
  AuthService                    → login/logout, récupération du profil courant, IsAdmin
  CollectionService               → CRUD collections
  BookService                     → CRUD livres + filtres
  IsbnLookupService               → appel Open Library / Google Books
  ImageUploadService              → compression (JS interop) + upload Storage + suppression

Models/
  Book.cs
  BookCollection.cs
  Profile.cs
```

## 7. Ordre de développement recommandé

1. Créer le projet Supabase, exécuter le schéma SQL (tables + RLS + trigger profil auto), créer le bucket Storage avec ses policies
2. Mettre en place le workflow GitHub Actions keep-alive dès cette étape (pour ne pas l'oublier)
3. Créer le projet Blazor WASM (template PWA), installer le package NuGet `Supabase`, configurer la connexion (URL + clé anon en config)
4. Implémenter l'authentification (login/logout, `CustomAuthenticationStateProvider`, récupération du rôle depuis `profiles`)
5. CRUD collections (liste, création, renommage, suppression — visible/actif uniquement pour admin)
6. CRUD livres sans ISBN d'abord (formulaire simple, upload photo avec compression, association à une collection)
7. Vue liste + vue tuile + filtres (nom, auteur, date)
8. Intégration du lookup ISBN (Open Library API) pour pré-remplissage
9. Gestion des utilisateurs par l'admin (changer les rôles)
10. Finitions PWA (manifest, icônes, test d'installation sur smartphone) + responsive/CSS (fond blanc, mobile-first)
11. Déploiement (Azure Static Web Apps ou GitHub Pages, avec SPA fallback)

## 8. Hors périmètre (v2 / plus tard, ne pas développer maintenant)

- Liste de souhaits / réservation de livres
- Scan de code-barres ISBN via la caméra
- Reconnaissance automatique de la couverture par photo (OCR/vision)
- Notifications, partage social, export, etc.

## 9. Informations à fournir à Claude Code au démarrage

- URL du projet Supabase + clé `anon` (à créer sur supabase.com, plan gratuit)
- Cible de déploiement souhaitée (GitHub Pages vs Azure Static Web Apps) — à confirmer si pas encore décidé
- Compte GitHub pour héberger le repo et faire tourner le workflow keep-alive
