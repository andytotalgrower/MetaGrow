const pulses = new WeakMap();

export function reveal(element) {
    if (!element?.isConnected) return;

    element.focus({ preventScroll: true });
    element.scrollIntoView({ behavior: "instant", block: "start", inline: "nearest" });

    pulses.get(element)?.cancel();
    pulses.delete(element);
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const pulse = element.animate([
        { boxShadow: "0 0 0 3px rgba(0, 124, 186, 0)" },
        { boxShadow: "0 0 0 3px rgba(0, 124, 186, 0.75)", offset: 0.5 },
        { boxShadow: "0 0 0 3px rgba(0, 124, 186, 0)" }
    ], { duration: 1000, iterations: 2, easing: "ease-in-out" });
    pulses.set(element, pulse);
}
