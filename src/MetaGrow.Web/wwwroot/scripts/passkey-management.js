function showToast(message, style = 'danger') {
    if (window.metaGrowPasskeyToast) {
        window.metaGrowPasskeyToast.invokeMethodAsync('Show', message, style);
        return;
    }
    window.metaGrowPasskeyToastQueue ??= [];
    window.metaGrowPasskeyToastQueue.push({ message, style });
}

function getErrorMessage(result, fallback) {
    if (Array.isArray(result.errors)) return result.errors.join(' ');
    if (result.errors && typeof result.errors === 'object') return Object.values(result.errors).flat().join(' ');
    return result.detail || result.title || fallback;
}

document.addEventListener('submit', async event => {
    const form = event.target.closest('[data-passkey-update-form]');
    if (!form) return;
    event.preventDefault();
    const host = form.closest('[data-passkey-management]');
    const button = event.submitter;
    const action = button?.getAttribute('value');
    const data = new FormData(form);
    const credentialId = data.get('CredentialId');
    button.disabled = true;
    try {
        const rename = action === 'rename';
        const response = await fetch(`/Account/Passkeys/${encodeURIComponent(credentialId)}`, {
            method: rename ? 'PUT' : 'DELETE', credentials: 'include',
            headers: {
                ...(rename ? { 'Content-Type': 'application/json' } : {}),
                [host.getAttribute('request-token-name')]: host.getAttribute('request-token-value')
            },
            body: rename ? JSON.stringify({ displayName: data.get('DisplayName') }) : undefined
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(getErrorMessage(result, `The passkey could not be ${rename ? 'renamed' : 'deleted'} (${response.status}).`));
        if (!rename) form.remove();
        showToast(result.message ?? `Passkey ${rename ? 'renamed' : 'deleted'}.`, 'success');
    } catch (error) {
        showToast(error.message);
    } finally {
        if (form.isConnected) button.disabled = false;
    }
});
