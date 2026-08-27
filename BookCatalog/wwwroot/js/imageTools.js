// Canvas-based image compression, used instead of a .NET imaging library to keep
// the WASM bundle small (brief 4.1). Never uploads a raw photo: every path here
// resizes to maxWidth and re-encodes as JPEG before the bytes reach Blazor.

async function compressBlob(blob, maxWidth, quality) {
    const imgBitmap = await createImageBitmap(blob);
    let width = imgBitmap.width;
    let height = imgBitmap.height;

    if (width > maxWidth) {
        height = Math.round(height * (maxWidth / width));
        width = maxWidth;
    }

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(imgBitmap, 0, 0, width, height);
    imgBitmap.close();

    const outBlob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
    const arrayBuffer = await outBlob.arrayBuffer();
    return new Uint8Array(arrayBuffer);
}

window.imageTools = {
    compressFromInputElement: async function (inputEl, maxWidth, quality) {
        if (!inputEl || !inputEl.files || inputEl.files.length === 0) {
            return null;
        }
        return await compressBlob(inputEl.files[0], maxWidth, quality);
    },

    // Best-effort: fetches an external cover image (e.g. Open Library) and compresses it.
    // Returns null instead of throwing if the source blocks cross-origin canvas reads.
    compressFromUrl: async function (url, maxWidth, quality) {
        try {
            const response = await fetch(url, { mode: 'cors' });
            if (!response.ok) {
                return null;
            }
            const blob = await response.blob();
            return await compressBlob(blob, maxWidth, quality);
        } catch {
            return null;
        }
    }
};
