namespace BookCatalog.Services;

/// <summary>
/// Drives the app-wide confirmation modal. Any component can call
/// <see cref="ConfirmAsync"/> and await the user's yes / no answer.
/// </summary>
public sealed class ConfirmService
{
    public ConfirmRequest? Current { get; private set; }

    public event Action? OnChange;

    /// <summary>
    /// Shows the modal and completes when the user confirms (<c>true</c>) or cancels (<c>false</c>).
    /// </summary>
    public Task<bool> ConfirmAsync(
        string message,
        string title = "Confirmer",
        string confirmLabel = "Supprimer",
        string cancelLabel = "Annuler",
        bool danger = true)
    {
        // A pending request loses to the newcomer.
        Current?.Complete(false);

        var request = new ConfirmRequest(message, title, confirmLabel, cancelLabel, danger);
        request.Closed += Clear;
        Current = request;
        OnChange?.Invoke();
        return request.Task;
    }

    private void Clear()
    {
        Current = null;
        OnChange?.Invoke();
    }

    public sealed class ConfirmRequest
    {
        private readonly TaskCompletionSource<bool> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConfirmRequest(string message, string title, string confirmLabel, string cancelLabel, bool danger)
        {
            Message = message;
            Title = title;
            ConfirmLabel = confirmLabel;
            CancelLabel = cancelLabel;
            Danger = danger;
        }

        public string Message { get; }
        public string Title { get; }
        public string ConfirmLabel { get; }
        public string CancelLabel { get; }
        public bool Danger { get; }

        internal Task<bool> Task => _tcs.Task;

        internal event Action? Closed;

        public void Complete(bool result)
        {
            if (_tcs.TrySetResult(result))
            {
                Closed?.Invoke();
            }
        }
    }
}
