// Browser-side helpers for the shot marker QR and the full-shoot upload sort.
// Heavy image work runs here, on the viewer's machine, not on the server.
// Libraries load on first use from pinned CDN URLs (see docs/photoshoot/POC-DECISIONS.md).

const LIBS = {
    qr: "https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js",
    jsqr: "https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js",
    exif: "https://cdn.jsdelivr.net/npm/exifr@7.1.3/dist/lite.umd.js"
};

const loading = {};

function loadScript(url) {
    if (!loading[url]) {
        loading[url] = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = url;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error("Could not load " + url));
            document.head.appendChild(script);
        });
    }

    return loading[url];
}

// Draws the marker QR into the element with the given id. Returns false when the element is not on the page.
export async function renderQr(elementId, text) {
    await loadScript(LIBS.qr);
    const element = document.getElementById(elementId);
    if (!element) {
        return false;
    }

    element.innerHTML = "";
    // eslint-disable-next-line no-undef
    new QRCode(element, { text: text, width: 220, height: 220, correctLevel: QRCode.CorrectLevel.M });
    return true;
}

// Measures one photo from its bytes: sharpness, exposure, clipping, a QR payload if one is visible, and EXIF capture time.
export async function analyse(bytes, fallbackIso) {
    await Promise.all([loadScript(LIBS.jsqr), loadScript(LIBS.exif)]);

    const blob = new Blob([bytes]);
    let capturedIso = fallbackIso || null;
    try {
        // eslint-disable-next-line no-undef
        const tags = await exifr.parse(blob, ["DateTimeOriginal"]);
        if (tags && tags.DateTimeOriginal) {
            capturedIso = new Date(tags.DateTimeOriginal).toISOString();
        }
    } catch (error) {
        // No EXIF: keep the fallback time.
    }

    const bitmap = await createImageBitmap(blob);
    const sourceWidth = bitmap.width;
    const sourceHeight = bitmap.height;
    const scale = Math.min(1, 1280 / Math.max(sourceWidth, sourceHeight));
    const w = Math.max(1, Math.round(sourceWidth * scale));
    const h = Math.max(1, Math.round(sourceHeight * scale));

    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(bitmap, 0, 0, w, h);
    if (bitmap.close) {
        bitmap.close();
    }

    const pixels = ctx.getImageData(0, 0, w, h).data;
    const count = w * h;
    const luma = new Float32Array(count);
    let sum = 0;
    let highlights = 0;
    let shadows = 0;
    for (let i = 0, p = 0; i < count; i++, p += 4) {
        const y = 0.299 * pixels[p] + 0.587 * pixels[p + 1] + 0.114 * pixels[p + 2];
        luma[i] = y;
        sum += y;
        if (y >= 250) highlights++;
        if (y <= 5) shadows++;
    }

    // Laplacian variance over the interior pixels: low values mean soft focus.
    let lapSum = 0;
    let lapSquares = 0;
    let lapCount = 0;
    for (let y = 1; y < h - 1; y++) {
        for (let x = 1; x < w - 1; x++) {
            const i = y * w + x;
            const lap = 4 * luma[i] - luma[i - 1] - luma[i + 1] - luma[i - w] - luma[i + w];
            lapSum += lap;
            lapSquares += lap * lap;
            lapCount++;
        }
    }
    const lapMean = lapCount ? lapSum / lapCount : 0;
    const variance = lapCount ? lapSquares / lapCount - lapMean * lapMean : 0;

    let qrText = null;
    try {
        // eslint-disable-next-line no-undef
        const code = jsQR(pixels, w, h);
        if (code && code.data) {
            qrText = code.data;
        }
    } catch (error) {
        qrText = null;
    }

    return {
        sharpness: Math.min(1, variance / 1000),
        meanLuma: sum / count / 255,
        clippedHighlights: highlights / count,
        clippedShadows: shadows / count,
        qrText: qrText,
        capturedIso: capturedIso,
        width: sourceWidth,
        height: sourceHeight
    };
}
