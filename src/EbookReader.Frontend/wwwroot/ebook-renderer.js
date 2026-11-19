/**
 * EbookReader Client-Side Rendering Engine.
 * Provides PDF rendering via Mozilla's PDF.js (with canvas and text layer),
 * CSS multi-column pagination for EPUB chapters, and theme switching.
 */
(function () {
    /**
     * Cache storing downloaded binary book ArrayBuffers keyed by book GUID string.
     * Prevents duplicate network fetches when navigating pages within the same book.
     */
    const fileCache = new Map();

    /**
     * Cache storing initialized PDF.js PDFDocumentProxy instances keyed by book GUID string.
     * Allows fast multi-page navigation without re-parsing the PDF binary data.
     */
    const pdfDocCache = new Map();

    /**
     * Monotonically increasing identifier used to invalidate stale asynchronous renders.
     */
    let currentRenderId = 0;

    /**
     * Metadata tracking the currently active EPUB chapter's multi-column pagination state.
     * @type {{ track: HTMLElement, stride: number, totalSubPages: number, currentSubPage: number } | null}
     */
    let activeEpubColumns = null;

    /**
     * Dynamically injects an external script element into the document head with a timeout safeguard.
     * @param {string} src - The URL of the script to load.
     * @param {number} [timeoutMs=3000] - Timeout in milliseconds before failing the load attempt.
     * @returns {Promise<void>} Resolves when the script loads or rejects on error or timeout.
     */
    function loadScript(src, timeoutMs = 3000) {
        return new Promise((resolve, reject) => {
            // Check if script tag is already present in document
            const existing = document.querySelector(`script[src="${src}"]`);
            if (existing) {
                resolve();
                return;
            }
            const script = document.createElement("script");
            script.src = src;
            script.async = true;

            // Enforce timeout safeguard against slow or unreachable CDNs
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

    /**
     * Promise tracking ongoing or completed initialization of the PDF.js library.
     * @type {Promise<any> | null}
     */
    let pdfjsPromise = null;

    /**
     * Ensures that the PDF.js library and its accompanying text layer stylesheet are loaded.
     * @returns {Promise<any>} Resolves to `window.pdfjsLib` if available, or null on failure.
     */
    async function ensurePdfJs() {
        const cssUrl = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf_viewer.min.css";
        // Dynamically append PDF.js text layer stylesheet if not already present
        if (!document.querySelector(`link[href="${cssUrl}"]`)) {
            const link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = cssUrl;
            document.head.appendChild(link);
        }

        // Return immediately if already available in global scope
        if (window.pdfjsLib) {
            return window.pdfjsLib;
        }

        // Initialize PDF.js asynchronously once
        if (!pdfjsPromise) {
            pdfjsPromise = (async () => {
                try {
                    await loadScript("https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js", 3000);
                    if (window.pdfjsLib) {
                        // Configure web worker path for background PDF parsing and decompression
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

    /**
     * Retrieves the binary ArrayBuffer for a book, fetching it over HTTP if not already cached.
     * @param {string} bookId - Unique book identifier.
     * @param {string} fileUrl - Absolute or relative URL to the raw book file endpoint.
     * @returns {Promise<ArrayBuffer>} The binary content of the book file.
     */
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

    /**
     * Global theme manager object for toggling between light and dark visual themes.
     */
    window.ebookTheme = {
        /**
         * Applies the specified theme mode to the document and any embedded iframes.
         * @param {boolean} isDark - True for dark mode, false for light mode.
         */
        apply: function (isDark) {
            const theme = isDark ? "dark" : "light";
            document.documentElement.setAttribute("data-theme", theme);
            try {
                localStorage.setItem("ebook_theme", theme);
            } catch (e) { }

            // If an iframe is currently used for rendering, inject dark mode overrides into its DOM
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

    // Bootstrap theme immediately on initial script execution from localStorage
    try {
        const savedTheme = localStorage.getItem("ebook_theme");
        if (savedTheme === "dark" || savedTheme === "light") {
            document.documentElement.setAttribute("data-theme", savedTheme);
        }
    } catch (e) { }

    /**
     * Renders a specific page of a PDF document using PDF.js canvas rendering,
     * falling back to an embedded iframe if PDF.js is unavailable or rendering fails.
     * @param {HTMLElement} target - The container DOM element.
     * @param {ArrayBuffer|null} arrayBuffer - The raw PDF binary buffer.
     * @param {object} options - Page rendering options.
     * @param {number} renderId - Token identifying the current render pass.
     * @returns {Promise<void>}
     */
    async function renderPdfPage(target, arrayBuffer, options, renderId) {
        // Clear active EPUB column pagination state when rendering PDF
        activeEpubColumns = null;
        const zoomFactor = (options.zoomLevel || 100) / 100.0;
        let renderedWithPdfJs = false;

        try {
            const pdfjs = await ensurePdfJs();
            if (pdfjs && arrayBuffer) {
                // Retrieve or parse PDF document model
                let pdfDoc = pdfDocCache.get(options.bookId);
                if (!pdfDoc) {
                    const loadingTask = pdfjs.getDocument({ data: new Uint8Array(arrayBuffer.slice(0)) });
                    pdfDoc = await loadingTask.promise;
                    pdfDocCache.set(options.bookId, pdfDoc);
                }

                // Check for concurrent rendering invalidation
                if (renderId !== currentRenderId) return;

                // Obtain the specified PDF page and calculate fit-to-container scale
                const page = await pdfDoc.getPage(options.pageNumber);
                const unscaledViewport = page.getViewport({ scale: 1.0 });

                const availableWidth = Math.max(300, (target.clientWidth || 900) - 24);
                const availableHeight = Math.max(300, (target.clientHeight || window.innerHeight || 800) - 24);
                const fitScale = Math.min(availableWidth / unscaledViewport.width, availableHeight / unscaledViewport.height);
                const cssScale = fitScale * zoomFactor;

                // Support HiDPI / Retina displays by supersampling canvas dimensions
                const pixelRatio = Math.max(window.devicePixelRatio || 1, 2);
                const cssViewport = page.getViewport({ scale: cssScale });
                const hiresViewport = page.getViewport({ scale: cssScale * pixelRatio });

                // Construct container and canvas elements
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

                // Text layer div overlay for selectable and searchable text
                const textLayerDiv = document.createElement("div");
                textLayerDiv.className = "textLayer";
                textLayerDiv.style.cssText = `position:absolute; inset:0; width:${cssViewport.width}px; height:${cssViewport.height}px;`;
                textLayerDiv.style.setProperty("--scale-factor", String(cssViewport.scale));

                pdfPageContainer.appendChild(canvas);
                pdfPageContainer.appendChild(textLayerDiv);
                wrapper.appendChild(pdfPageContainer);
                target.appendChild(wrapper);

                // Render page graphic to canvas
                const ctx = canvas.getContext("2d");
                await page.render({ canvasContext: ctx, viewport: hiresViewport }).promise;

                // Check again for concurrent render cancellation
                if (renderId !== currentRenderId) return;

                // Render text layer for copy/paste and accessibility
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

        // Fallback to native browser PDF viewer iframe or fallback HTML
        if (!renderedWithPdfJs) {
            if (renderId !== currentRenderId) return;
            if (arrayBuffer && arrayBuffer.byteLength > 800) {
                target.innerHTML = `<iframe src="${options.fileUrl}#page=${options.pageNumber}&view=Fit" style="width:100%; height:100%; border:none;"></iframe>`;
            } else {
                target.innerHTML = options.fallbackHtml || "";
            }
        }
    }

    /**
     * Renders an EPUB chapter into a paginated multi-column horizontal track.
     * @param {HTMLElement} target - The container DOM element.
     * @param {object} options - Page rendering options including subPage and zoomLevel.
     * @returns {{ subPage: number, subPageCount: number }} Current sub-page and total sub-page count.
     */
    function renderEpubPage(target, options) {
        target.innerHTML = "";
        activeEpubColumns = null;

        const zoomFactor = (options.zoomLevel || 100) / 100.0;
        // Outer paper sheet resembling physical page margins
        const sheet = document.createElement("div");
        sheet.className = "epub-page-sheet";
        sheet.style.cssText = "width: min(1080px, 98%); height: calc(100vh - 1.5rem); background: var(--reader-paper, #fffef9); color: var(--reader-text, #0f172a); border-radius: 0.5rem; box-shadow: 0 2px 10px rgba(0,0,0,0.08); padding: 1.5rem 2.25rem; overflow: hidden; margin: auto; box-sizing: border-box;";

        // Viewport masking columns horizontally
        const viewport = document.createElement("div");
        viewport.style.cssText = "width: 100%; height: 100%; overflow: hidden; position: relative;";
        sheet.appendChild(viewport);
        target.appendChild(sheet);

        const colWidth = Math.max(240, viewport.clientWidth);
        const colHeight = Math.max(200, viewport.clientHeight);
        const colGap = 48;
        const stride = colWidth + colGap;

        // Multi-column track formatting chapter content into horizontal screens
        const track = document.createElement("div");
        track.className = "epub-column-track";
        track.style.cssText = `width: ${colWidth}px; height: ${colHeight}px; column-width: ${colWidth}px; column-gap: ${colGap}px; column-fill: auto; font-family: Georgia, 'Times New Roman', serif; font-size: ${(1.06 * zoomFactor).toFixed(2)}rem; line-height: 1.55; transition: transform 0.15s ease;`;

        // Strip embedded <style> tags to avoid overriding reader theming
        const cleanHtml = (options.fallbackHtml || "").replace(/<style[^>]*>[\s\S]*?<\/style>/gi, "");
        track.innerHTML = cleanHtml;
        viewport.appendChild(track);

        // Compute total column sub-pages based on generated scrollWidth
        const totalSubPages = Math.max(1, Math.ceil((track.scrollWidth - 8) / stride));
        let subIndex;
        // Negative subPage indicates navigation backwards into the last sub-page of the chapter
        if (options.subPage < 0) {
            subIndex = totalSubPages - 1;
        } else {
            subIndex = Math.min(Math.max(0, (options.subPage || 1) - 1), totalSubPages - 1);
        }

        // Shift track to show the selected sub-page
        track.style.transform = `translateX(-${subIndex * stride}px)`;
        activeEpubColumns = { track, stride, totalSubPages, currentSubPage: subIndex + 1 };
        return { subPage: subIndex + 1, subPageCount: totalSubPages };
    }

    /**
     * Public interop API for Blazor C# components.
     */
    window.ebookRenderer = {
        /**
         * Renders a book page or chapter into the specified DOM target element.
         * @param {object} options - Options containing bookId, fileUrl, format, pageNumber, subPage, zoomLevel, targetId.
         * @returns {Promise<{ subPage: number, subPageCount: number }>} Sub-page navigation information.
         */
        renderPage: async function (options) {
            const renderId = ++currentRenderId;
            const target = document.getElementById(options.targetId);
            if (!target) return { subPage: 1, subPageCount: 1 };

            const format = (options.format || "").toUpperCase();
            let arrayBuffer = null;

            // Fetch file binary data for client-side rendering
            try {
                arrayBuffer = await getFileArrayBuffer(options.bookId, options.fileUrl);
            } catch (err) {
                console.warn("Could not fetch file ArrayBuffer:", err);
            }

            // Stale render check after network request
            if (renderId !== currentRenderId) return { subPage: 1, subPageCount: 1 };

            // Dispatch rendering based on book format
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

        /**
         * Navigates to a specific sub-page within the currently loaded EPUB chapter without full re-render.
         * @param {number} subPage - 1-based sub-page index.
         * @returns {{ subPage: number, subPageCount: number }} Updated sub-page state.
         */
        setEpubSubPage: function (subPage) {
            if (!activeEpubColumns || !activeEpubColumns.track) {
                return { subPage: 1, subPageCount: 1 };
            }
            const total = activeEpubColumns.totalSubPages;
            const targetSubPage = Math.min(Math.max(1, subPage), total);
            const subIndex = targetSubPage - 1;
            // Shift horizontal track offset
            activeEpubColumns.track.style.transform = `translateX(-${subIndex * activeEpubColumns.stride}px)`;
            activeEpubColumns.currentSubPage = targetSubPage;
            return { subPage: targetSubPage, subPageCount: total };
        }
    };
})();
