// Keep DevExpress, custom page styles and native controls on the same saved colour scheme.
window.mantineScheme = {
    get: function () {
        const match = document.cookie.match(/(?:^|; )mantine-color-scheme=([^;]*)/);
        return match && match[1] === 'dark' ? 'dark' : 'light';
    },
    set: function (scheme) {
        const value = scheme === 'dark' ? 'dark' : 'light';
        document.cookie = 'mantine-color-scheme=' + value + '; path=/; max-age=31536000; SameSite=Lax';
        this.applyBody(value === 'dark');
    },
    applyBody: function (dark) {
        // Root state also reaches popups rendered outside the page's scoped CSS.
        document.documentElement.setAttribute('data-bs-theme', dark ? 'dark' : 'light');
        // CSS supplies color-scheme from this attribute, allowing print styles to use light controls.
        document.body?.classList.toggle('mantine-dark', dark);
    },
    restore: function () { this.applyBody(this.get() === 'dark'); }
};
// Apply before the first paint, then again after the server-rendered body exists.
window.mantineScheme.restore();
