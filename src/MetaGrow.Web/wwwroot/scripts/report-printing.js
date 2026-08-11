window.metaGrowReportPrinting = {
    pendingTimer: null,

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

            const brandLogos = Array.from(document.querySelectorAll(
                ".report-cover-logo, .section-brand-logo, .handout-brand-logo"));
            await Promise.all(brandLogos.map(async function (image) {
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
