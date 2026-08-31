export function register(reference) {
    window.metaGrowPasskeyToast = reference;
    const pending = window.metaGrowPasskeyToastQueue ?? [];
    window.metaGrowPasskeyToastQueue = [];
    for (const item of pending) reference.invokeMethodAsync('Show', item.message, item.style);
}

export function unregister(reference) {
    if (window.metaGrowPasskeyToast === reference) delete window.metaGrowPasskeyToast;
}
