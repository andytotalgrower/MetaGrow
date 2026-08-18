window.metaGrowNewTabNavigation = {
    tabs: {},

    reserve: function (key) {
        const existing = this.tabs[key];
        if (existing && !existing.closed) {
            existing.close();
        }

        const tab = window.open("about:blank", key);
        this.tabs[key] = tab;

        if (tab) {
            tab.document.title = "Generating survey | MetaGrow";
            tab.document.body.innerHTML = "<main style=\"font-family:system-ui,sans-serif;padding:2rem;color:#243447\"><h1 style=\"font-size:1.25rem\">Generating Sample survey…</h1><p>This tab will open the survey when it is ready.</p></main>";
        }
    },

    navigate: function (key, url) {
        const tab = this.tabs[key];
        delete this.tabs[key];

        if (tab && !tab.closed) {
            tab.location.replace(url);
            tab.focus();
            return true;
        }

        return window.open(url, "_blank", "noopener") !== null;
    },

    close: function (key) {
        const tab = this.tabs[key];
        delete this.tabs[key];
        if (tab && !tab.closed) {
            tab.close();
        }
    }
};
