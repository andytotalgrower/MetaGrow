window.metaGrowFilterPreferences = {
    get: function (name) {
        const prefix = encodeURIComponent(name) + "=";
        const cookie = document.cookie
            .split(";")
            .map(value => value.trim())
            .find(value => value.startsWith(prefix));

        return cookie ? decodeURIComponent(cookie.substring(prefix.length)) : null;
    },

    set: function (name, value) {
        let cookie = encodeURIComponent(name) + "=" + encodeURIComponent(value)
            + "; path=/; max-age=31536000; SameSite=Lax";

        if (window.location.protocol === "https:") {
            cookie += "; Secure";
        }

        document.cookie = cookie;
    }
};
