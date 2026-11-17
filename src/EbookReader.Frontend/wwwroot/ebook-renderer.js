(function () {
    const fileCache = new Map();
    const pdfDocCache = new Map();
    let currentRenderId = 0;
    let activeEpubColumns = null;

    function loadScript(src, timeoutMs = 3000) {
        return new Promise((resolve, reject) => {
            const existing = document.querySelector(`script[src="${src}"]`);
            if (existing) {
                resolve();
                return;
            }
            const script = document.createElement("script");
            script.src = src;
            script.async = true;

            const timer = setTimeout(() => {
                script.onerror = null;
                script.onload = null;
                reject(new Error(`Timeout loading script: ${src}`));
            }, timeoutMs);

            script.onload = () => {
                clearTimeout(timer);
                resolve();
            };
            script.onerror = (err) => {
                clearTimeout(timer);
                reject(err || new Error(`Error loading script: ${src}`));
            };
            document.head.appendChild(script);
        });
    }

    let pdfjsPromise = null;
    async function ensurePdfJs() {
        const cssUrl = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf_viewer.min.css";
        if (!document.querySelector(`link[href="${cssUrl}"]`)) {
            const link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = cssUrl;
            document.head.appendChild(link);
        }

        if (window.pdfjsLib) {
            return window.pdfjsLib;
        }
        if (!pdfjsPromise) {
            pdfjsPromise = (async () => {
                try {
                    await loadScript("https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js", 3000);
                    if (window.pdfjsLib) {
                        window.pdfjsLib.GlobalWorkerOptions.workerSrc = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js";
                        return window.pdfjsLib;
                    }
                } catch (e) {
                    console.warn("PDF.js dynamic load failed / timed out:", e);
                }
                return null;
            })();
        }
        return pdfjsPromise;
    }

    async function getFileArrayBuffer(bookId, fileUrl) {
        if (fileCache.has(bookId)) {
            return fileCache.get(bookId);
        }
        const resp = await fetch(fileUrl);
        if (!resp.ok) {
            throw new Error(`Fetch failed: ${resp.status} ${resp.statusText}`);
        }
        const buffer = await resp.arrayBuffer();
        fileCache.set(bookId, buffer);
        return buffer;
    }

    window.ebookTheme = {
        apply: function (isDark) {
            const theme = isDark ? "dark" : "light";
            document.documentElement.setAttribute("data-theme", theme);
            try {
                localStorage.setItem("ebook_theme", theme);
            } catch (e) { }

            const iframe = document.querySelector("#ebook-render-target iframe");
            if (iframe && iframe.contentWindow && iframe.contentWindow.document) {
                try {
                    const doc = iframe.contentWindow.document;
                    let styleEl = doc.getElementById("ebook-dark-mode-override");
                    if (isDark) {
                        if (!styleEl) {
                            styleEl = doc.createElement("style");
                            styleEl.id = "ebook-dark-mode-override";
                            doc.head.appendChild(styleEl);
                        }
                        styleEl.textContent = ":root{color-scheme:dark;} body{background:#1e293b !important; color:#e2e8f0 !important;} p,div,span,h1,h2,h3,h4,li,a{color:#e2e8f0 !important;}";
                    } else if (styleEl) {
                        styleEl.remove();
                    }
                } catch (e) { }
            }
        }
    };

    try {
        const savedTheme = localStorage.getItem("ebook_theme");
        if (savedTheme === "dark" || savedTheme === "light") {
            document.documentElement.setAttribute("data-theme", savedTheme);
        }
    } catch (e) { }

    async function renderPdfPage(target, arrayBuffer, options, renderId) {
        activeEpubColumns = null;
        const zoomFactor = (options.zoomLevel || 100) / 100.0;
        let renderedWithPdfJs = false;

        try {
            const pdfjs = await ensurePdfJs();
            if (pdfjs && arrayBuffer) {
                let pdfDoc = pdfDocCache.get(options.bookId);
                if (!pdfDoc) {
                    const loadingTask = pdfjs.getDocument({ data: new Uint8Array(arrayBuffer.slice(0)) });
                    pdfDoc = await loadingTask.promise;
                    pdfDocCache.set(options.bookId, pdfDoc);
                }

                if (renderId !== currentRenderId) return;

                const page = await pdfDoc.getPage(options.pageNumber);
                const unscaledViewport = page.getViewport({ scale: 1.0 });

                const availableWidth = Math.max(300, (target.clientWidth || 900) - 24);
                const availableHeight = Math.max(300, (target.clientHeight || window.innerHeight || 800) - 24);
                const fitScale = Math.min(availableWidth / unscaledViewport.width, availableHeight / unscaledViewport.height);
                const cssScale = fitScale * zoomFactor;

                const pixelRatio = Math.max(window.devicePixelRatio || 1, 2);
                const cssViewport = page.getViewport({ scale: cssScale });
                const hiresViewport = page.getViewport({ scale: cssScale * pixelRatio });

                target.innerHTML = "";
                const wrapper = document.createElement("div");
                wrapper.style.cssText = "display:flex; justify-content:center; align-items:center; width:100%; height:100%; min-height:100%; overflow:auto; padding:0.5rem;";

                const pdfPageContainer = document.createElement("div");
                pdfPageContainer.className = "pdf-page-container";
                pdfPageContainer.style.cssText = `position:relative; width:${cssViewport.width}px; height:${cssViewport.height}px; margin:auto; flex-shrink:0; box-shadow:0 2px 10px rgba(0,0,0,0.12); background:#fff; text-align:initial; line-height:1;`;
                pdfPageContainer.style.setProperty("--scale-factor", String(cssViewport.scale));

                const canvas = document.createElement("canvas");
                canvas.width = hiresViewport.width;
                canvas.height = hiresViewport.height;
                canvas.style.width = cssViewport.width + "px";
                canvas.style.height = cssViewport.height + "px";
                canvas.style.display = "block";

                const textLayerDiv = document.createElement("div");
                textLayerDiv.className = "textLayer";
                textLayerDiv.style.cssText = `position:absolute; inset:0; width:${cssViewport.width}px; height:${cssViewport.height}px;`;
                textLayerDiv.style.setProperty("--scale-factor", String(cssViewport.scale));

                pdfPageContainer.appendChild(canvas);
                pdfPageContainer.appendChild(textLayerDiv);
                wrapper.appendChild(pdfPageContainer);
                target.appendChild(wrapper);

                const ctx = canvas.getContext("2d");
                await page.render({ canvasContext: ctx, viewport: hiresViewport }).promise;

                if (renderId !== currentRenderId) return;

                const textContent = await page.getTextContent();
                if (pdfjs.renderTextLayer) {
                    const textTask = pdfjs.renderTextLayer({
                        textContentSource: textContent,
                        container: textLayerDiv,
                        viewport: cssViewport,
                        textDivs: []
                    });
                    if (textTask && textTask.promise) {
                        await textTask.promise;
                    }
                }

                renderedWithPdfJs = true;
            }
        } catch (pdfErr) {
            console.warn("PDF.js render failed, falling back:", pdfErr);
        }

        if (!renderedWithPdfJs) {
            if (renderId !== currentRenderId) return;
            if (arrayBuffer && arrayBuffer.byteLength > 800) {
                target.innerHTML = `<iframe src="${options.fileUrl}#page=${options.pageNumber}&view=Fit" style="width:100%; height:100%; border:none;"></iframe>`;
            } else {
                target.innerHTML = options.fallbackHtml || "";
            }
        }
    }

    function renderEpubPage(target, options) {
        target.innerHTML = "";
        activeEpubColumns = null;

        const zoomFactor = (options.zoomLevel || 100) / 100.0;
        const sheet = document.createElement("div");
        sheet.className = "epub-page-sheet";
        sheet.style.cssText = "width: min(1080px, 98%); height: calc(100vh - 1.5rem); background: var(--reader-paper, #fffef9); color: var(--reader-text, #0f172a); border-radius: 0.5rem; box-shadow: 0 2px 10px rgba(0,0,0,0.08); padding: 1.5rem 2.25rem; overflow: hidden; margin: auto; box-sizing: border-box;";

        const viewport = document.createElement("div");
        viewport.style.cssText = "width: 100%; height: 100%; overflow: hidden; position: relative;";
        sheet.appendChild(viewport);
        target.appendChild(sheet);

        const colWidth = Math.max(240, viewport.clientWidth);
        const colHeight = Math.max(200, viewport.clientHeight);
        const colGap = 48;
        const stride = colWidth + colGap;

        const track = document.createElement("div");
        track.className = "epub-column-track";
        track.style.cssText = `width: ${colWidth}px; height: ${colHeight}px; column-width: ${colWidth}px; column-gap: ${colGap}px; column-fill: auto; font-family: Georgia, 'Times New Roman', serif; font-size: ${(1.06 * zoomFactor).toFixed(2)}rem; line-height: 1.55; transition: transform 0.15s ease;`;

        const cleanHtml = (options.fallbackHtml || "").replace(/<style[^>]*>[\s\S]*?<\/style>/gi, "");
        track.innerHTML = cleanHtml;
        viewport.appendChild(track);

        const totalSubPages = Math.max(1, Math.ceil((track.scrollWidth - 8) / stride));
        let subIndex;
        if (options.subPage < 0) {
            subIndex = totalSubPages - 1;
        } else {
            subIndex = Math.min(Math.max(0, (options.subPage || 1) - 1), totalSubPages - 1);
        }

        track.style.transform = `translateX(-${subIndex * stride}px)`;
        activeEpubColumns = { track, stride, totalSubPages, currentSubPage: subIndex + 1 };
        return { subPage: subIndex + 1, subPageCount: totalSubPages };
    }

    window.ebookRenderer = {
        renderPage: async function (options) {
            const renderId = ++currentRenderId;
            const target = document.getElementById(options.targetId);
            if (!target) return { subPage: 1, subPageCount: 1 };

            const format = (options.format || "").toUpperCase();
            let arrayBuffer = null;

            try {
                arrayBuffer = await getFileArrayBuffer(options.bookId, options.fileUrl);
            } catch (err) {
                console.warn("Could not fetch file ArrayBuffer:", err);
            }

            if (renderId !== currentRenderId) return { subPage: 1, subPageCount: 1 };

            if (format === "PDF") {
                await renderPdfPage(target, arrayBuffer, options, renderId);
                return { subPage: 1, subPageCount: 1 };
            } else if (format === "EPUB") {
                return renderEpubPage(target, options);
            } else {
                target.innerHTML = options.fallbackHtml || "";
                return { subPage: 1, subPageCount: 1 };
            }
        },
        setEpubSubPage: function (subPage) {
            if (!activeEpubColumns || !activeEpubColumns.track) {
                return { subPage: 1, subPageCount: 1 };
            }
            const total = activeEpubColumns.totalSubPages;
            const targetSubPage = Math.min(Math.max(1, subPage), total);
            const subIndex = targetSubPage - 1;
            activeEpubColumns.track.style.transform = `translateX(-${subIndex * activeEpubColumns.stride}px)`;
            activeEpubColumns.currentSubPage = targetSubPage;
            return { subPage: targetSubPage, subPageCount: total };
        }
    };
})();
