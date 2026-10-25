// Zoomable skill tree: a port of the interaction in docs/superpowers/specs/mockups/skills-tree.html.
// The SVG's viewBox is owned by this script; Blazor renders the same static viewBox and never patches it.
const S = 640, MIN = S / 10;
const states = new WeakMap();

const state = svg => states.get(svg);

function apply(svg) {
    const st = state(svg);
    if (!st) return;
    const vb = st.vb;
    svg.setAttribute("viewBox", `${vb.x} ${vb.y} ${vb.w} ${vb.h}`);
    const lvl = svg.closest(".tree-panel")?.querySelector(".zoomlvl");
    if (lvl) lvl.textContent = (S / vb.w).toFixed(1) + "×";
}

function toSvg(svg, cx, cy) {
    const p = svg.createSVGPoint();
    p.x = cx; p.y = cy;
    return p.matrixTransform(svg.getScreenCTM().inverse());
}

function zoomAt(svg, px, py, f) {
    const st = state(svg), vb = st.vb;
    const w = Math.min(S, Math.max(MIN, vb.w * f)), k = w / vb.w;
    st.vb = { x: px - (px - vb.x) * k, y: py - (py - vb.y) * k, w, h: w };
    apply(svg);
}

function animateTo(svg, target) {
    const st = state(svg), from = { ...st.vb }, t0 = performance.now();
    const step = t => {
        const a = Math.min(1, (t - t0) / 350), e = 1 - Math.pow(1 - a, 3);
        const w = from.w + (target.w - from.w) * e;
        st.vb = { x: from.x + (target.x - from.x) * e, y: from.y + (target.y - from.y) * e, w, h: w };
        apply(svg);
        if (a < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
}

// The hover card follows the cursor (mockup rule: +16/+12, flipped left by 270 when it would pass the right edge).
// Positions go into CSS variables on the panel; --px/--py keep where the last click landed for the rejection message.
function place(svg, e, prefix) {
    const panel = svg.closest(".tree-panel");
    if (!panel) return;
    const r = panel.getBoundingClientRect();
    let left = e.clientX - r.left + 16;
    const top = e.clientY - r.top + 12;
    if (left + 240 > r.width) left -= 270;
    panel.style.setProperty(`--${prefix}x`, left + "px");
    panel.style.setProperty(`--${prefix}y`, top + "px");
}

export function zoomBy(svg, factor) {
    const vb = state(svg).vb;
    zoomAt(svg, vb.x + vb.w / 2, vb.y + vb.h / 2, factor);
}

export function fit(svg) { animateTo(svg, { x: 0, y: 0, w: S }); }

export function flyTo(svg, x, y) {
    const w = S / 3.2;
    animateTo(svg, { x: x - w / 2, y: y - w / 2, w });
}

export function attach(svg, dotnetRef) {
    detach(svg);
    const st = { vb: { x: 0, y: 0, w: S, h: S }, drag: null, moved: false, hover: 0, off: [] };
    states.set(svg, st);
    const on = (type, fn, opts) => { svg.addEventListener(type, fn, opts); st.off.push(() => svg.removeEventListener(type, fn, opts)); };
    const nodeId = el => { const n = el?.closest?.(".nd"); return n ? Number(n.dataset.id) : 0; };
    const hover = id => { if (id !== st.hover) { st.hover = id; dotnetRef.invokeMethodAsync("NodeHovered", id); } };

    on("wheel", e => {
        e.preventDefault();
        const p = toSvg(svg, e.clientX, e.clientY);
        zoomAt(svg, p.x, p.y, Math.exp(e.deltaY * 0.0015));
    }, { passive: false });

    on("pointerdown", e => {
        st.drag = { x: e.clientX, y: e.clientY, vb: { ...st.vb } };
        st.moved = false;
        svg.setPointerCapture(e.pointerId);
    });
    on("pointermove", e => {
        if (!st.drag) return;
        const r = svg.getBoundingClientRect(), u = st.vb.w / Math.min(r.width, r.height);
        const dx = e.clientX - st.drag.x, dy = e.clientY - st.drag.y;
        if (Math.abs(dx) + Math.abs(dy) > 3) { st.moved = true; svg.classList.add("drag"); hover(0); }
        if (st.moved) { st.vb = { ...st.vb, x: st.drag.vb.x - dx * u, y: st.drag.vb.y - dy * u }; apply(svg); }
    });
    on("pointerup", e => {
        svg.classList.remove("drag");
        if (st.drag && !st.moved) {
            const el = document.elementFromPoint(e.clientX, e.clientY);
            const id = nodeId(el);
            if (id) place(svg, e, "p");
            if (id) dotnetRef.invokeMethodAsync("NodeClicked", id);
            else if (el && el.classList.contains("lab")) flyTo(svg, +el.getAttribute("x"), +el.getAttribute("y"));
        }
        st.drag = null;
    });
    on("pointercancel", () => { st.drag = null; svg.classList.remove("drag"); });

    const over = e => { if (!st.drag) { hover(nodeId(e.target)); place(svg, e, "t"); } };
    on("mouseover", over);
    on("mousemove", over);
    on("mouseout", e => { if (nodeId(e.target)) hover(0); });
    apply(svg);
}

export function detach(svg) {
    const st = states.get(svg);
    if (!st) return;
    st.off.forEach(f => f());
    states.delete(svg);
}
