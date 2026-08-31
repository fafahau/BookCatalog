// Lets the browser Back button (and the Android system back gesture) dismiss the
// app-wide fullscreen image viewer instead of navigating away from the page.
//
// When the viewer opens we push a throwaway history entry; the next Back pops it
// and we close the viewer. When the viewer is closed from the UI (✕ or tapping
// the backdrop) we call history.back() ourselves to discard that entry, so the
// user's real history stays clean.
window.bookcatalogImageViewer = {
    _dotnet: null,
    _open: false,

    // `dotnet` is a DotNetObjectReference<ImageViewerService>.
    register: function (dotnet) {
        this._dotnet = dotnet;
        window.addEventListener('popstate', () => {
            if (!this._open) {
                return;
            }
            this._open = false;
            this._dotnet.invokeMethodAsync('CloseFromHistory');
        });
    },

    // Called when the viewer opens.
    pushState: function () {
        if (this._open) {
            return;
        }
        this._open = true;
        history.pushState({ bookcatalogImageViewer: true }, '');
    },

    // Called when the viewer is closed from the UI: undo our history entry.
    popState: function () {
        if (!this._open) {
            return;
        }
        this._open = false;
        history.back();
    }
};
