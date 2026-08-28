// Supabase's password-recovery redirect lands back on the app with the session
// tokens (or an error) in the URL hash fragment, e.g.
//   /reset-password#access_token=...&refresh_token=...&type=recovery
//   /reset-password#error=access_denied&error_description=Email+link+is+invalid
// Blazor routing ignores the fragment, so we read it here and hand it to .NET.
window.authRedirect = {
    // Returns the raw fragment (without the leading '#') and strips it from the
    // address bar / history so the tokens don't linger after we've consumed them.
    consumeHash: function () {
        const raw = window.location.hash || '';
        const fragment = raw.startsWith('#') ? raw.substring(1) : raw;
        if (fragment) {
            history.replaceState(null, '', window.location.pathname + window.location.search);
        }
        return fragment;
    }
};
