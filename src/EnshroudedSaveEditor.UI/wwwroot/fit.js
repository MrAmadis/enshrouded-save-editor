// Scales a screen to the window with CSS zoom, keeping its proportions (owner, 2026-10-26: "resize everything to fit
// the window size"). "fill": the element grows or shrinks to its parent's width and the window's height below its top.
// "dialog": it takes up to 96 % by 92 % of the window. Blazor calls update after each render; a window resize does too.
const tracked = new Map();
const MIN_ZOOM = 0.5, MAX_ZOOM = 4;

function room(el, mode) {
    if (mode === "dialog") return { w: innerWidth * 0.96, h: innerHeight * 0.92 };
    const parent = el.parentElement, cs = getComputedStyle(parent);
    return {
        w: parent.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight),
        h: innerHeight - el.getBoundingClientRect().top - parseFloat(cs.paddingBottom),
    };
}

export function update(el) {
    const mode = tracked.get(el);
    if (!mode) return;
    el.style.zoom = "1";                                   // measure the natural size
    const size = el.getBoundingClientRect(), r = room(el, mode);
    if (size.width <= 0 || size.height <= 0) return;
    const z = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Math.min(r.w / size.width, r.h / size.height)));
    el.style.zoom = z.toFixed(3);
}

const updateAll = () => tracked.forEach((_, el) => update(el));

export function attach(el, mode) {
    tracked.set(el, mode);
    el.classList.add("fitted");
    if (tracked.size === 1) addEventListener("resize", updateAll);
    update(el);
}

export function detach(el) {
    if (!tracked.delete(el)) return;
    el.style.zoom = "";
    el.classList.remove("fitted");
    if (tracked.size === 0) removeEventListener("resize", updateAll);
}
