// Full-screen crop modal. No external dependency and canvas-only, like
// imageTools.js, to keep the JS footprint small. Pointer-events based so a
// drag works the same with touch or mouse.
//
// window.imageCropper.open(dataUrl, maxWidth, quality)
//   -> Promise<Uint8Array>  cropped + re-encoded JPEG bytes
//   -> Promise<null>         the user cancelled

(function () {
    const MIN = 40; // smallest crop box side, in screen pixels

    function clamp(v, lo, hi) {
        return Math.max(lo, Math.min(hi, v));
    }

    window.imageCropper = {
        open: function (imageDataUrl, maxWidth, quality) {
            return new Promise((resolve) => {
                const img = new Image();
                img.onload = () => build(img, maxWidth, quality, resolve);
                img.onerror = () => resolve(null);
                img.src = imageDataUrl;
            });
        }
    };

    function build(img, maxWidth, quality, resolve) {
        const overlay = document.createElement('div');
        overlay.className = 'cropper-overlay';
        overlay.innerHTML = `
            <div class="cropper-stage">
              <div class="cropper-box">
                <div class="cropper-h" data-h="nw"></div>
                <div class="cropper-h" data-h="ne"></div>
                <div class="cropper-h" data-h="sw"></div>
                <div class="cropper-h" data-h="se"></div>
                <div class="cropper-h" data-h="n"></div>
                <div class="cropper-h" data-h="s"></div>
                <div class="cropper-h" data-h="w"></div>
                <div class="cropper-h" data-h="e"></div>
              </div>
            </div>
            <div class="cropper-bar">
              <button type="button" class="cropper-cancel">Annuler</button>
              <button type="button" class="cropper-reset">Tout</button>
              <button type="button" class="cropper-ok primary">Valider</button>
            </div>`;

        const stage = overlay.querySelector('.cropper-stage');
        const box = overlay.querySelector('.cropper-box');
        img.className = 'cropper-img';
        stage.insertBefore(img, box);
        document.body.appendChild(overlay);

        let imgRect; // displayed image box, relative to the stage
        let b;       // crop box {x, y, w, h}, relative to the stage

        function measure() {
            const s = stage.getBoundingClientRect();
            const r = img.getBoundingClientRect();
            imgRect = { left: r.left - s.left, top: r.top - s.top, width: r.width, height: r.height };
        }

        function render() {
            box.style.left = b.x + 'px';
            box.style.top = b.y + 'px';
            box.style.width = b.w + 'px';
            box.style.height = b.h + 'px';
        }

        function resetBox() {
            measure();
            b = { x: imgRect.left, y: imgRect.top, w: imgRect.width, h: imgRect.height };
            render();
        }

        requestAnimationFrame(resetBox);
        window.addEventListener('resize', resetBox);

        let drag = null; // { mode, start:{x,y}, box:{...} }

        function pointFromEvent(e) {
            const s = stage.getBoundingClientRect();
            return { x: e.clientX - s.left, y: e.clientY - s.top };
        }

        function onDown(e) {
            const handle = e.target.getAttribute('data-h');
            if (!handle && e.target !== box) {
                return;
            }
            e.preventDefault();
            drag = { mode: handle || 'move', start: pointFromEvent(e), box: { ...b } };
            window.addEventListener('pointermove', onMove);
            window.addEventListener('pointerup', onUp);
        }

        function onMove(e) {
            if (!drag) {
                return;
            }
            const p = pointFromEvent(e);
            const dx = p.x - drag.start.x;
            const dy = p.y - drag.start.y;
            const o = drag.box;
            const minX = imgRect.left;
            const minY = imgRect.top;
            const maxX = imgRect.left + imgRect.width;
            const maxY = imgRect.top + imgRect.height;

            if (drag.mode === 'move') {
                b.x = clamp(o.x + dx, minX, maxX - o.w);
                b.y = clamp(o.y + dy, minY, maxY - o.h);
            } else {
                let x1 = o.x;
                let y1 = o.y;
                let x2 = o.x + o.w;
                let y2 = o.y + o.h;
                if (drag.mode.includes('w')) { x1 = clamp(o.x + dx, minX, x2 - MIN); }
                if (drag.mode.includes('e')) { x2 = clamp(o.x + o.w + dx, x1 + MIN, maxX); }
                if (drag.mode.includes('n')) { y1 = clamp(o.y + dy, minY, y2 - MIN); }
                if (drag.mode.includes('s')) { y2 = clamp(o.y + o.h + dy, y1 + MIN, maxY); }
                b.x = x1;
                b.y = y1;
                b.w = x2 - x1;
                b.h = y2 - y1;
            }
            render();
        }

        function onUp() {
            drag = null;
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
        }

        box.addEventListener('pointerdown', onDown);

        function cleanup() {
            window.removeEventListener('resize', resetBox);
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            overlay.remove();
        }

        overlay.querySelector('.cropper-cancel').onclick = () => { cleanup(); resolve(null); };
        overlay.querySelector('.cropper-reset').onclick = resetBox;
        overlay.querySelector('.cropper-ok').onclick = () => {
            const scale = img.naturalWidth / imgRect.width;
            const sx = clamp((b.x - imgRect.left) * scale, 0, img.naturalWidth);
            const sy = clamp((b.y - imgRect.top) * scale, 0, img.naturalHeight);
            const sw = clamp(b.w * scale, 1, img.naturalWidth - sx);
            const sh = clamp(b.h * scale, 1, img.naturalHeight - sy);

            let outW = sw;
            let outH = sh;
            if (outW > maxWidth) {
                outH = Math.round(outH * (maxWidth / outW));
                outW = maxWidth;
            }

            const canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(outW));
            canvas.height = Math.max(1, Math.round(outH));
            canvas.getContext('2d').drawImage(img, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);
            canvas.toBlob(async (blob) => {
                const buf = await blob.arrayBuffer();
                cleanup();
                resolve(new Uint8Array(buf));
            }, 'image/jpeg', quality);
        };
    }
})();
