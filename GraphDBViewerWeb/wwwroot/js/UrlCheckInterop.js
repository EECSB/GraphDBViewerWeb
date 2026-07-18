//Availability probe for a pasted image / 3D-model URL. Returns { reachable, error } — error carries
//the exact reason (an HTTP status like "HTTP 404 Not Found", or a network / CORS message) so the UI can
//show it. Images use an <img> probe (loads cross-origin without CORS, but exposes no HTTP status); other
//files use fetch (which, like the 3D model loader itself, needs the host to send CORS headers — so a
//"not reachable" result also means the app wouldn't be able to load it).
window.urlCheck = {
    isReachable: function (url, kind) {
        if (!url || !url.trim())
            return Promise.resolve({ reachable: false, error: "No URL entered." });

        url = url.trim();

        if (kind === 'image') {
            return new Promise(function (resolve) {
                var done = false;
                var finish = function (result) {
                    if (!done) {
                        done = true;
                        resolve(result);
                    }
                };

                var img = new Image();
                img.onload = function () { finish({ reachable: true, error: "" }); };
                img.onerror = function () { finish({ reachable: false, error: "Image could not be loaded — the URL may be wrong, blocked, or not an image. (An <img> probe can't read the HTTP status.)" }); };
                setTimeout(function () { finish({ reachable: false, error: "Timed out loading the image (12s)." }); }, 12000);
                img.src = url;
            });
        }

        return (async function () {
            var controller = new AbortController();
            var timer = setTimeout(function () { controller.abort(); }, 12000);

            try {
                var resp = await fetch(url, { method: 'GET', signal: controller.signal });
                clearTimeout(timer);

                //Got the headers — we don't need the body, so stop the download.
                try { if (resp.body) resp.body.cancel(); } catch (e) { }

                if (resp.ok)
                    return { reachable: true, error: "" };

                return { reachable: false, error: "HTTP " + resp.status + (resp.statusText ? " " + resp.statusText : "") };
            } catch (e) {
                clearTimeout(timer);

                var msg;
                if (e && e.name === 'AbortError')
                    msg = "Timed out (12s).";
                else if (e && e.message)
                    msg = e.message;
                else
                    msg = "Request failed.";

                return { reachable: false, error: msg + " — the host may not send CORS headers, so the app can't load it either." };
            }
        })();
    }
};
