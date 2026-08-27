// Camera ISBN scanning. Uses the browser's native BarcodeDetector when available
// (Android Chrome), otherwise lazy-loads the vendored ZXing UMD bundle. The raw
// ZXing library is ~330 KB, so it is only fetched the first time a scan starts.

const ZXING_SRC = 'js/vendor/zxing-0.21.3.min.js';

let zxingPromise = null;
function loadZxing() {
    if (window.ZXing) {
        return Promise.resolve(window.ZXing);
    }
    if (!zxingPromise) {
        zxingPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = ZXING_SRC;
            script.onload = () => resolve(window.ZXing);
            script.onerror = () => { zxingPromise = null; reject(new Error('ZXing failed to load')); };
            document.head.appendChild(script);
        });
    }
    return zxingPromise;
}

// EAN-13 book codes start with 978/979 (Bookland); also accept a bare ISBN-10.
function looksLikeIsbn(code) {
    const digits = (code || '').replace(/[^0-9Xx]/g, '');
    return (digits.length === 13 && (digits.startsWith('978') || digits.startsWith('979')))
        || digits.length === 10;
}

let native = null;        // { detector, stream, video, raf, stopped }
let zxingReader = null;   // ZXing BrowserMultiFormatReader instance

async function startNative(video, onResult) {
    const formats = await window.BarcodeDetector.getSupportedFormats();
    if (!formats.includes('ean_13')) {
        return false;
    }

    const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' } }
    });

    const detector = new window.BarcodeDetector({ formats: ['ean_13', 'ean_8', 'upc_a', 'upc_e'] });
    const state = { detector, stream, video, raf: 0, stopped: false };
    native = state;

    video.srcObject = stream;
    video.setAttribute('playsinline', 'true');
    await video.play();

    const tick = async () => {
        if (state.stopped) {
            return;
        }
        try {
            const codes = await detector.detect(video);
            const hit = codes.find(c => looksLikeIsbn(c.rawValue));
            if (hit) {
                onResult(hit.rawValue);
                return;
            }
        } catch {
            // transient decode error; keep scanning
        }
        state.raf = requestAnimationFrame(tick);
    };
    state.raf = requestAnimationFrame(tick);
    return true;
}

async function startZxing(video, onResult) {
    const ZXing = await loadZxing();
    const hints = new Map();
    hints.set(ZXing.DecodeHintType.POSSIBLE_FORMATS, [
        ZXing.BarcodeFormat.EAN_13,
        ZXing.BarcodeFormat.EAN_8,
        ZXing.BarcodeFormat.UPC_A,
        ZXing.BarcodeFormat.UPC_E
    ]);

    zxingReader = new ZXing.BrowserMultiFormatReader(hints, 300);
    await zxingReader.decodeFromConstraints(
        { video: { facingMode: { ideal: 'environment' } } },
        video,
        (result) => {
            if (result && looksLikeIsbn(result.getText())) {
                onResult(result.getText());
            }
        }
    );
}

window.barcodeScanner = {
    // Returns 'native' | 'zxing' on success, or throws (no camera / permission denied).
    start: async function (video, dotNetRef) {
        await window.barcodeScanner.stop();

        const onResult = (code) => {
            window.barcodeScanner.stop();
            dotNetRef.invokeMethodAsync('OnBarcodeDetected', code);
        };

        if ('BarcodeDetector' in window) {
            try {
                if (await startNative(video, onResult)) {
                    return 'native';
                }
            } catch (e) {
                await window.barcodeScanner.stop();
                throw e;
            }
        }

        await startZxing(video, onResult);
        return 'zxing';
    },

    stop: async function () {
        if (native) {
            native.stopped = true;
            if (native.raf) {
                cancelAnimationFrame(native.raf);
            }
            if (native.stream) {
                native.stream.getTracks().forEach(t => t.stop());
            }
            if (native.video) {
                native.video.srcObject = null;
            }
            native = null;
        }
        if (zxingReader) {
            try {
                zxingReader.reset();
            } catch {
                // already stopped
            }
            zxingReader = null;
        }
    }
};
