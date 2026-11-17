window.metaGrowReportPrinting = {
    pendingTimer: null,
    _photoLayoutCookie: "metagrow-print-photo-layout",

    // Both survey reports use the same browser preference, whether opened normally or for direct printing.
    getPhotoLayout: function () {
        const saved = window.metaGrowFilterPreferences.get(this._photoLayoutCookie);
        if (["none", "best-fit", "1", "2", "3"].includes(saved)) return saved;
        // Preserve the old checkbox choice for browsers upgrading to the layout selector.
        return window.metaGrowFilterPreferences.get("metagrow-print-photos") === "true" ? "best-fit" : "none";
    },

    setPhotoLayout: function (layout) {
        const value = ["none", "best-fit", "1", "2", "3"].includes(layout) ? layout : "none";
        window.metaGrowFilterPreferences.set(this._photoLayoutCookie, value);
    },

    _fitPhotoRows: function () {
        for (const grid of document.querySelectorAll(".include-print-photos .photo-grid")) {
            const columns = Number(grid.style.getPropertyValue("--print-photo-columns")) || 3;
            const rows = Number(grid.style.getPropertyValue("--print-photo-rows")) || 2;
            // Measure the complete captions at their printed width, including wrapped comments.
            const measure = document.createElement("div");
            measure.setAttribute("aria-hidden", "true");
            measure.style.cssText = "position:fixed;left:-10000px;visibility:hidden;width:273mm;font:8pt/1.3 Arial,sans-serif";
            document.body.appendChild(measure);
            try {
                const pixelsPerMm = measure.getBoundingClientRect().width / 273;
                measure.style.width = ((273 - (columns - 1) * 3) / columns) + "mm";
                let captionHeight = 0;
                for (const caption of grid.querySelectorAll("figcaption")) {
                    const copy = caption.cloneNode(true);
                    copy.style.cssText = "display:flex;flex-direction:column;gap:1mm;padding:2mm;overflow-wrap:anywhere";
                    for (const child of copy.children) {
                        child.style.fontSize = "inherit";
                        child.style.whiteSpace = "pre-line";
                    }
                    measure.appendChild(copy);
                    captionHeight = Math.max(captionHeight, copy.getBoundingClientRect().height / pixelsPerMm);
                    copy.remove();
                }
                // Leave room for the photo heading on the first sheet. Extra-long captions may
                // flow onto more sheets, with their full text and each photo kept together.
                const rowHeight = (148 - (rows - 1) * 3) / rows;
                const imageHeight = Math.max(20, Math.min(rows === 1 ? 126 : 58, rowHeight - captionHeight - 1));
                grid.style.setProperty("--print-photo-height", imageHeight + "mm");
            }
            finally {
                measure.remove();
            }
        }
    },

    print: function (fileName, delayMilliseconds) {
        const delay = Number.isFinite(delayMilliseconds)
            ? Math.max(0, delayMilliseconds)
            : 0;

        if (this.pendingTimer !== null) {
            window.clearTimeout(this.pendingTimer);
        }

        this.pendingTimer = window.setTimeout(async function () {
            window.metaGrowReportPrinting.pendingTimer = null;

            if (document.fonts && document.fonts.ready) {
                try {
                    await document.fonts.ready;
                }
                catch {
                    // Font readiness is an enhancement; printing can still proceed.
                }
            }

            const printImages = Array.from(document.querySelectorAll(
                ".report-cover-logo, .section-brand-logo, .handout-brand-logo, " +
                ".survey-print-report.include-print-photos .photo-grid img"));
            await Promise.all(printImages.map(async function (image) {
                // Off-screen survey photos may still be deferred when the user starts printing.
                if (image.loading === "lazy") {
                    image.loading = "eager";
                }

                if (!image.complete) {
                    await new Promise(resolve => {
                        image.addEventListener("load", resolve, { once: true });
                        image.addEventListener("error", resolve, { once: true });
                    });
                }

                if (typeof image.decode === "function") {
                    try {
                        await image.decode();
                    }
                    catch {
                        // A failed decode must not leave the user without printing.
                    }
                }
            }));

            window.metaGrowReportPrinting._fitPhotoRows();

            // Give DevExpress and the browser two completed paint frames before
            // capturing the document for print.
            await new Promise(resolve => window.requestAnimationFrame(
                () => window.requestAnimationFrame(resolve)));

            const originalTitle = document.title;
            const suggestedName = String(fileName || "metagrow-report.pdf").replace(/\.pdf$/i, "");
            let restored = false;

            const restoreTitle = function () {
                if (restored) return;
                restored = true;
                document.title = originalTitle;
                window.removeEventListener("afterprint", restoreTitle);
            };

            document.title = suggestedName;
            window.addEventListener("afterprint", restoreTitle, { once: true });

            try {
                window.print();
            }
            finally {
                // Some browsers do not raise afterprint. The print dialog has already
                // captured the title by the time this fallback restores the tab title.
                window.setTimeout(restoreTitle, 1000);
            }
        }, delay);

        // The print dialog is deliberately opened by the queued callback so this
        // JavaScript interop call returns before Chrome blocks on window.print().
        return true;
    }
};
