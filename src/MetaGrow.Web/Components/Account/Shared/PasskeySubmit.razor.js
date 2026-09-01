const supported = typeof navigator.credentials !== 'undefined' && typeof PublicKeyCredential !== 'undefined' &&
    typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function' && typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

function showToast(message, style = 'danger') {
    if (window.metaGrowPasskeyToast) {
        window.metaGrowPasskeyToast.invokeMethodAsync('Show', message, style);
        return;
    }
    window.metaGrowPasskeyToastQueue ??= [];
    window.metaGrowPasskeyToastQueue.push({ message, style });
}

async function errorMessage(response, fallback) {
    const text = await response.text();
    if (!text) return fallback;
    try {
        const result = JSON.parse(text);
        if (Array.isArray(result.errors)) return result.errors.join(' ');
        if (result.errors && typeof result.errors === 'object') return Object.values(result.errors).flat().join(' ');
        return result.detail || result.title || text;
    } catch { return text; }
}

customElements.define('passkey-submit', class extends HTMLElement {
    static formAssociated = true;
    connectedCallback() {
        this.internals = this.attachInternals();
        this.internals.form.addEventListener('submit', event => {
            if (event.submitter?.name === '__passkeySubmit') { event.preventDefault(); this.run(); }
        });
    }
    disconnectedCallback() { this.controller?.abort(); }
    get tokenName() { return this.getAttribute('request-token-name'); }
    get tokenValue() { return this.getAttribute('request-token-value'); }
    async completeCreation(ceremony, credential, displayName) {
        const response = await fetch('/Account/Passkeys/Register', {
            method: 'POST', credentials: 'include',
            headers: { 'Content-Type': 'application/json', [this.tokenName]: this.tokenValue },
            body: JSON.stringify({ ceremonyId: ceremony.ceremonyId, credentialJson: JSON.stringify(credential), displayName })
        });
        if (!response.ok) throw new Error(await errorMessage(response, `The passkey could not be saved (${response.status}).`));
        const result = await response.json();
        showToast(result.message ?? 'Passkey added.', 'success');
        setTimeout(() => window.location.reload(), 600);
    }
    async submitLogin(output) {
        this.internals.setFormValue(output);
        const form = this.internals.form;
        const data = new FormData(form);
        data.set(this.getAttribute('form-token-name'), this.tokenValue);
        const response = await fetch(form.action || window.location.href, {
            method: form.method || 'POST', credentials: 'include',
            headers: { [this.tokenName]: this.tokenValue }, body: data, redirect: 'follow'
        });
        if (!response.ok) throw new Error(`The passkey could not be submitted (${response.status}).`);
        if (response.redirected) { window.location.assign(response.url); return; }
        const html = await response.text();
        document.open(); document.write(html); document.close();
    }
    async run() {
        this.controller?.abort(); this.controller = new AbortController();
        try {
            if (!supported) throw new Error('Passkeys are not supported by this browser.');
            const form = new FormData(this.internals.form), operation = this.getAttribute('operation');
            const displayName = operation === 'Create' ? form.get(this.getAttribute('display-name-name')) : null;
            const username = operation === 'Create' ? null : (form.get(this.getAttribute('email-name')) || '').trim();
            const body = operation === 'Create' ? { displayName } : (username ? { username } : {});
            const url = operation === 'Create' ? '/Account/Passkeys/CreationOptions' : '/Account/Passkeys/RequestOptions';
            const response = await fetch(url, { method: 'POST', credentials: 'include', signal: this.controller.signal,
                headers: { 'Content-Type': 'application/json', [this.tokenName]: this.tokenValue }, body: JSON.stringify(body) });
            if (!response.ok) throw new Error(await errorMessage(response, `Request failed (${response.status}).`));
            const ceremony = await response.json(), options = JSON.parse(ceremony.optionsJson);
            const credential = operation === 'Create'
                ? await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options), signal: this.controller.signal })
                : await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options), signal: this.controller.signal });
            if (operation === 'Create') { await this.completeCreation(ceremony, credential, displayName); return; }
            const output = new FormData(), name = this.getAttribute('name');
            output.append(`${name}.CeremonyId`, ceremony.ceremonyId);
            output.append(`${name}.CredentialJson`, JSON.stringify(credential));
            await this.submitLogin(output);
        } catch (error) {
            if (error.name === 'AbortError') return;
            const cancelled = error.name === 'NotAllowedError';
            showToast(cancelled ? 'Passkey setup was cancelled.' : error.message, cancelled ? 'warning' : 'danger');
        }
    }
});
