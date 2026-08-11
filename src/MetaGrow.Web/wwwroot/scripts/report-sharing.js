window.metaGrowReportSharing = {
    getFragmentToken: function () {
        return window.location.hash.length > 1
            ? decodeURIComponent(window.location.hash.substring(1))
            : "";
    },
    copyText: async function (value) {
        await navigator.clipboard.writeText(value);
    },
    renderQr: function (elementId, value) {
        const element = document.getElementById(elementId);
        if (!element || !window.QRCode) return false;
        element.innerHTML = "";
        new QRCode(element, { text: value, width: 240, height: 240 });
        return true;
    },
    downloadQr: function (elementId, fileName) {
        const element = document.getElementById(elementId);
        const canvas = element ? element.querySelector("canvas") : null;
        const image = element ? element.querySelector("img") : null;
        const source = canvas ? canvas.toDataURL("image/png") : image ? image.src : null;
        if (!source) return false;
        const link = document.createElement("a");
        link.href = source;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        return true;
    }
};
