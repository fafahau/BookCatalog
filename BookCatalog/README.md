# BookCatalog

Application PWA (Blazor WebAssembly + Supabase) pour cataloguer les livres physiques d'une famille, organisés en collections. Voir `BookCatalog-brief-claude-code.md` (à la racine du dépôt, au-dessus de ce dossier) pour le cahier des charges complet.

## Stack

- Blazor WebAssembly (.NET 8), PWA
- Supabase (Postgres + Auth + Storage), appelé directement depuis le navigateur via le package NuGet `Supabase`
- Hébergement : GitHub Pages (workflow fourni ; Azure Static Web Apps documenté en alternative plus bas)

## 1. Configurer le projet Supabase

1. Dans le [dashboard Supabase](https://supabase.com/dashboard), ouvrez le projet `https://yukbvkvusufxawhstpiq.supabase.co`.
2. **SQL Editor** → New query → collez tout le contenu de [`sql/schema.sql`](sql/schema.sql) → Run.
   Ce script crée les tables `profiles`/`collections`/`books`, active la RLS avec les policies superadmin/admin/readonly, crée le trigger qui génère automatiquement un profil (`role = 'readonly'`) à chaque inscription, et crée le bucket Storage `book-photos` avec ses policies.
3. **Authentication → URL Configuration** :
   - **Site URL** : l'URL de déploiement finale (ex. `https://<user>.github.io/<repo>/`), sinon les liens de confirmation d'e-mail redirigeront vers `localhost`.
   - **Redirect URLs** : ajoutez les URL vers lesquelles les liens e-mail peuvent renvoyer. La réinitialisation de mot de passe renvoie vers `.../reset-password`, donc ajoutez au minimum :
     - `https://<user>.github.io/<repo>/reset-password`
     - `http://localhost:5000/reset-password` et l'URL HTTPS locale (ex. `https://localhost:7xxx/reset-password`) pour les tests en dev
     Les jokers sont acceptés (ex. `https://<user>.github.io/<repo>/*`).
4. Créez votre premier compte depuis l'application (page "Créer un compte"), puis promouvez-le `superadmin` en SQL Editor :
   ```sql
   update public.profiles set role = 'superadmin' where id = '<uuid de auth.users>';
   ```
   (l'UUID est visible dans **Authentication → Users**). Les comptes suivants pourront être promus `admin` (ou `superadmin` par un superadmin) depuis la page **Utilisateurs** de l'app.

   Rôles : `readonly` (lecture seule) < `admin` (gestion du catalogue et des utilisateurs) < `superadmin` (identique à `admin`, mais son profil ne peut pas être retiré depuis la page Utilisateurs et seul un autre superadmin peut changer son rôle). Pour retirer ou rétrograder le dernier superadmin, passez par le SQL Editor.

## 2. Configuration de l'app

L'URL et la clé `anon`/publishable Supabase sont déjà renseignées dans [`wwwroot/appsettings.json`](wwwroot/appsettings.json). C'est une clé publique par design (protégée par la RLS côté serveur) : rien de sensible n'est exposé.

## 3. Lancer en local

```bash
dotnet restore
dotnet run
```

Ouvrez l'URL affichée (ex. `https://localhost:7xxx`). Pour tester l'installation PWA, servez la version publiée (`dotnet publish` puis un serveur statique) car le service worker n'est actif qu'en `Release`/publish.

## 4. Déploiement — GitHub Pages (par défaut)

Le workflow [`​.github/workflows/deploy.yml`](.github/workflows/deploy.yml) publie l'app sur GitHub Pages à chaque push sur `main`.

1. Poussez ce dépôt sur GitHub.
2. **Settings → Pages → Build and deployment → Source** : choisissez **GitHub Actions**.
3. Le workflow adapte automatiquement le `<base href>` au nom du dépôt (`/<repo>/`) — aucune configuration manuelle n'est nécessaire même si le nom du dépôt change.
4. Après le premier déploiement, mettez à jour le **Site URL** Supabase (étape 1.3) avec l'URL Pages réelle.

### Alternative : Azure Static Web Apps

Si vous préférez Azure plutôt que GitHub Pages :

1. Créez une Static Web App (plan Free) sur Azure, reliée à ce dépôt.
2. Azure génère un workflow GitHub Actions (`azure-static-web-apps-*.yml`) — configurez-y :
   - `app_location: "BookCatalog"`
   - `output_location: "wwwroot"`
   - `api_location: ""` (pas de backend .NET serveur)
3. Le token de déploiement est ajouté automatiquement en secret GitHub par Azure.
4. Supprimez ou désactivez `deploy.yml` (GitHub Pages) pour éviter deux déploiements concurrents, et mettez à jour le Site URL Supabase avec l'URL `*.azurestaticapps.net`.

## 5. Empêcher la pause automatique du projet Supabase (plan gratuit)

Le plan gratuit met en pause tout projet resté 7 jours sans requête. Le workflow [`.github/workflows/keepalive.yml`](.github/workflows/keepalive.yml) exécute une requête légère tous les 3 jours.

Ajoutez ces secrets dans **Settings → Secrets and variables → Actions** du dépôt GitHub :

| Secret | Valeur |
|---|---|
| `SUPABASE_URL` | `https://yukbvkvusufxawhstpiq.supabase.co` |
| `SUPABASE_ANON_KEY` | la clé anon/publishable (celle de `appsettings.json`) |

**Si le ping échoue et que le projet se met quand même en pause** : dashboard Supabase → sélectionner le projet → bandeau "Project paused" → **Restore project**. Les données ne sont pas perdues, seule l'instance est redémarrée (délai de quelques minutes).

## 6. Réinitialisation du mot de passe

Depuis la page de connexion, **« Mot de passe oublié ? »** demande l'e-mail et appelle
`ResetPasswordForEmail` (flow implicite). Supabase envoie un e-mail dont le lien renvoie
vers `/reset-password` avec les jetons de récupération dans le fragment d'URL ; la page
[`Pages/ResetPassword.razor`](Pages/ResetPassword.razor) lit ce fragment via
[`wwwroot/js/authRedirect.js`](wwwroot/js/authRedirect.js), ouvre une session temporaire,
laisse choisir un nouveau mot de passe (`Auth.Update`), puis déconnecte pour forcer une
reconnexion.

Prérequis côté Supabase :

- **Redirect URLs** doit lister l'URL `.../reset-password` (voir étape 1.3), sinon Supabase
  refuse la redirection.
- **Authentication → Email Templates → Reset Password** : le template par défaut convient
  (`{{ .ConfirmationURL }}`). Le lien pointe vers `.../auth/v1/verify?...&redirect_to=.../reset-password`.
- Le champ mot de passe de connexion et celui de `reset-password` ont un bouton
  **Afficher / Masquer**.

## 7. PWA

L'app est installable ("Ajouter à l'écran d'accueil" sur mobile, icône d'installation dans la barre d'adresse sur desktop). Le manifest et le service worker sont générés par le template Blazor WASM PWA (`wwwroot/manifest.webmanifest`, `wwwroot/service-worker*.js`).

## Limitations connues

- **Suppression complète d'un compte** : la page Utilisateurs ne peut que révoquer l'accès (suppression de la ligne `profiles`), car supprimer un compte `auth.users` nécessite la clé `service_role`, qui ne doit jamais être exposée dans une app cliente WASM. Pour une suppression complète, utilisez **Authentication → Users** dans le dashboard Supabase.
- **Bucket Storage public** : `book-photos` est un bucket public afin de stocker de simples URLs publiques dans `books.photo_url_1/2` (brief 4.2). Les policies RLS du bucket restent en place (upload/suppression réservés aux admins), mais la route de lecture publique de Supabase ne passe pas par la RLS — acceptable ici vu la sensibilité faible de couvertures de livres dans une app familiale.

## Hors périmètre (v2)

Liste de souhaits/réservation, scan de code-barres caméra, reconnaissance de couverture par vision, notifications/partage/export — voir le cahier des charges.
