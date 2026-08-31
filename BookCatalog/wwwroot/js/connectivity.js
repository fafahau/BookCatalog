// Bridges the browser's online/offline state into .NET. The app itself keeps
// working offline (PWA shell + cached library snapshot); this just lets the UI
// show a banner and stop trying to reach Supabase when there's no network.
window.bookcatalogConnectivity = {
    isOnline: function () {
        return navigator.onLine;
    },

    // `dotnet` is a DotNetObjectReference<OfflineLibraryService>; we call back
    // into it whenever connectivity flips.
    register: function (dotnet) {
        function push() {
            dotnet.invokeMethodAsync('OnConnectivityChanged', navigator.onLine);
        }
        window.addEventListener('online', push);
        window.addEventListener('offline', push);
    }
};
