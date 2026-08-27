using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BookCatalog.Services;

/// <summary>
/// Compresses images via canvas JS interop (brief 4.1) and uploads the resulting JPEG
/// bytes to Supabase Storage under the book-photos/{collection_id}/{book_id}/photoN.jpg
/// convention (brief 4.3).
/// </summary>
public class ImageUploadService
{
    private const string BucketName = "book-photos";
    private const int MaxWidthPx = 1200;
    private const double JpegQuality = 0.8;

    private readonly Supabase.Client _client;
    private readonly IJSRuntime _js;

    public ImageUploadService(SupabaseService supabaseService, IJSRuntime js)
    {
        _client = supabaseService.Client;
        _js = js;
    }

    /// <summary>Reads the file selected in a plain &lt;input type="file"&gt; and returns resized/re-encoded JPEG bytes.</summary>
    public async Task<byte[]?> CompressFromInputElementAsync(ElementReference inputElement)
    {
        return await _js.InvokeAsync<byte[]?>("imageTools.compressFromInputElement", inputElement, MaxWidthPx, JpegQuality);
    }

    /// <summary>Best-effort compression of an external cover image URL (e.g. from the ISBN lookup). Returns null if it fails (CORS, network, ...).</summary>
    public async Task<byte[]?> CompressFromUrlAsync(string url)
    {
        return await _js.InvokeAsync<byte[]?>("imageTools.compressFromUrl", url, MaxWidthPx, JpegQuality);
    }

    private static string PathFor(Guid collectionId, Guid bookId, int slot) =>
        $"{collectionId}/{bookId}/photo{slot}.jpg";

    /// <summary>Uploads (or overwrites) the photo for the given slot (1 or 2) and returns its public URL.</summary>
    public async Task<string> UploadPhotoAsync(Guid collectionId, Guid bookId, int slot, byte[] jpegBytes)
    {
        var path = PathFor(collectionId, bookId, slot);
        var bucket = _client.Storage.From(BucketName);

        await bucket.Upload(jpegBytes, path, new Supabase.Storage.FileOptions
        {
            ContentType = "image/jpeg",
            Upsert = true
        });

        return bucket.GetPublicUrl(path);
    }

    public async Task DeleteBookPhotosAsync(Guid collectionId, Guid bookId)
    {
        var bucket = _client.Storage.From(BucketName);
        await bucket.Remove(new List<string>
        {
            PathFor(collectionId, bookId, 1),
            PathFor(collectionId, bookId, 2)
        });
    }
}
