(function () {
    const fileCache = new Map();
    const pdfDocCache = new Map();
    const activeEpub = {
        bookId: null,
        targetEl: null,
        book: null,
        rendition: null
    };

    let currentRenderId = 0;

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

    let epubPromise = null;
    async function ensureEpubJs() {
        if (window.ePub) {
            return window.ePub;
        }
        if (!epubPromise) {
            epubPromise = (async () => {
                try {
                    await loadScript("https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js", 3000);
                    await loadScript("https://cdn.jsdelivr.net/npm/epubjs@0.3.93/dist/epub.min.js", 3000);
                    if (window.ePub) {
                        return window.ePub;
                    }
                } catch (e) {
                    console.warn("epub.js dynamic load failed / timed out:", e);
                }
                return null;
            })();
        }
        return epubPromise;
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

    function renderEpubFallback(target, fallbackHtml) {
        target.innerHTML = "";
        const iframe = document.createElement("iframe");
        iframe.style.cssText = "width:100%; min-height:600px; border:none;";
        iframe.srcdoc = fallbackHtml || "";
        iframe.onload = () => {
            try {
                if (iframe.contentWindow && iframe.contentWindow.document && iframe.contentWindow.document.documentElement) {
                    const scrollHeight = iframe.contentWindow.document.documentElement.scrollHeight;
                    iframe.style.height = (scrollHeight + 32) + "px";
                }
            } catch (e) {
                // Ignore any cross-origin/sandbox height errors
            }
        };
        target.appendChild(iframe);
    }

    window.ebookRenderer = {
        renderPage: async function (options) {
            const renderId = ++currentRenderId;
            const target = document.getElementById(options.targetId);
            if (!target) return;

            const format = (options.format || "").toUpperCase();
            let arrayBuffer = null;

            try {
                arrayBuffer = await getFileArrayBuffer(options.bookId, options.fileUrl);
            } catch (err) {
                console.warn("Could not fetch file ArrayBuffer:", err);
            }

            if (renderId !== currentRenderId) return;

            if (format === "PDF") {
                // Destroy any lingering EPUB rendition when viewing a PDF
                if (activeEpub.book) {
                    try { activeEpub.book.destroy(); } catch (e) { }
                    activeEpub.book = null;
                    activeEpub.rendition = null;
                    activeEpub.bookId = null;
                    activeEpub.targetEl = null;
                }

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
                        const style = window.getComputedStyle(target);
                        const padLeft = parseFloat(style.paddingLeft) || 0;
                        const padRight = parseFloat(style.paddingRight) || 0;
                        const availableWidth = Math.max(300, (target.clientWidth || 800) - padLeft - padRight - 16);
                        const unscaledViewport = page.getViewport({ scale: 1.0 });
                        const cssScale = availableWidth / unscaledViewport.width;
                        const pixelRatio = Math.max(window.devicePixelRatio || 1, 2);
                        const cssViewport = page.getViewport({ scale: cssScale });
                        const hiresViewport = page.getViewport({ scale: cssScale * pixelRatio });

                        target.innerHTML = "";
                        const wrapper = document.createElement("div");
                        wrapper.style.cssText = "display:block; text-align:center; background:#f8fafc; padding:0.5rem; border-radius:0.5rem; overflow-x:auto;";

                        const pdfPageContainer = document.createElement("div");
                        pdfPageContainer.className = "pdf-page-container";
                        pdfPageContainer.style.cssText = `position:relative; width:${cssViewport.width}px; height:${cssViewport.height}px; margin:0 auto; box-shadow:0 2px 8px rgba(0,0,0,0.08); background:#fff; text-align:initial; line-height:1;`;
                        pdfPageContainer.style.setProperty("--scale-factor", String(cssViewport.scale));

                        const canvas = document.createElement("canvas");
                        canvas.width = hiresViewport.width;
                        canvas.height = hiresViewport.height;
                        canvas.style.width = cssViewport.width + "px";
                        canvas.style.height = cssViewport.height + "px";
                        canvas.style.display = "block";

                        const textLayerDiv = document.createElement("div");
                        textLayerDiv.className = "textLayer";
                        textLayerDiv.style.cssText = "position:absolute; inset:0; width:" + cssViewport.width + "px; height:" + cssViewport.height + "px;";
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
                        target.innerHTML = `<iframe src="${options.fileUrl}#page=${options.pageNumber}&view=FitH" style="width:100%; height:78vh; border:none; border-radius:0.5rem;"></iframe>`;
                    } else {
                        target.innerHTML = options.fallbackHtml || "";
                    }
                }
            } else if (format === "EPUB") {
                let renderedWithEpubJs = false;
                try {
                    const epubjs = await ensureEpubJs();
                    if (epubjs && arrayBuffer) {
                        const spineIndex = Math.max(0, options.pageNumber - 1);
                        const existingFrame = document.getElementById("epubjs-frame");

                        if (activeEpub.bookId === options.bookId &&
                            activeEpub.targetEl === target &&
                            activeEpub.rendition &&
                            activeEpub.book &&
                            existingFrame) {
                            const spineItem = activeEpub.book.spine.get(spineIndex);
                            await activeEpub.rendition.display(spineItem ? spineItem.href : spineIndex);
                            renderedWithEpubJs = true;
                        } else {
                            if (activeEpub.book) {
                                try {
                                    activeEpub.book.destroy();
                                } catch (e) { }
                                activeEpub.book = null;
                                activeEpub.rendition = null;
                            }

                            target.innerHTML = "";
                            const frameDiv = document.createElement("div");
                            frameDiv.id = "epubjs-frame";
                            frameDiv.style.cssText = "width:100%; min-height:600px;";
                            target.appendChild(frameDiv);

                            const book = window.ePub(arrayBuffer.slice(0));
                            const rendition = book.renderTo("epubjs-frame", { width: "100%", flow: "scrolled-doc" });

                            rendition.hooks.content.register(contents => {
                                contents.addStylesheetRules({
                                    "body": { "font-family": "Georgia, serif", "line-height": "1.65", "padding": "0.5rem 1rem" },
                                    "p": { "margin-top": "0.25em", "margin-bottom": "0.25em" },
                                    "img": { "max-width": "100%", "height": "auto" }
                                });
                            });

                            await book.ready;
                            if (renderId !== currentRenderId) return;

                            activeEpub.bookId = options.bookId;
                            activeEpub.targetEl = target;
                            activeEpub.book = book;
                            activeEpub.rendition = rendition;

                            const spineItem = book.spine.get(spineIndex);
                            await rendition.display(spineItem ? spineItem.href : spineIndex);
                            renderedWithEpubJs = true;
                        }
                    }
                } catch (epubErr) {
                    console.warn("epub.js render failed, falling back:", epubErr);
                }

                if (!renderedWithEpubJs) {
                    if (renderId !== currentRenderId) return;
                    renderEpubFallback(target, options.fallbackHtml);
                }
            } else {
                target.innerHTML = options.fallbackHtml || "";
            }
        }
    };
})();
