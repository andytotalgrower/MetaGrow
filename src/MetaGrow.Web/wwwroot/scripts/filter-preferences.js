window.metaGrowFilterPreferences = {
    get: function (name) {
        const prefix = encodeURIComponent(name) + "=";
        const cookie = document.cookie
            .split(";")
            .map(value => value.trim())
            .find(value => value.startsWith(prefix));

        return cookie ? decodeURIComponent(cookie.substring(prefix.length)) : null;
    },

    // Read a family of preferences at once while keeping each value in its own cookie.
    getByPrefix: function (prefix) {
        const encodedPrefix = encodeURIComponent(prefix);
        const preferences = Object.create(null);

        for (const entry of document.cookie.split(";")) {
            const cookie = entry.trim();
            const separator = cookie.indexOf("=");
            if (!cookie.startsWith(encodedPrefix) || separator < encodedPrefix.length) {
                continue;
            }

            try {
                const key = decodeURIComponent(cookie.slice(encodedPrefix.length, separator));
                preferences[key] = decodeURIComponent(cookie.slice(separator + 1));
            } catch {
                // One malformed cookie should leave the other preferences available.
            }
        }

        return preferences;
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
