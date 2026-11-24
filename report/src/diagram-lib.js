// Мінімальний рендерер діаграм у SVG (виконується в headless Chrome).
// spec = { width, height, groups[], nodes[], edges[], notes[] } або { kind: "seq", ... }.
(function () {
  const C = {
    ink: "#1f1f1d", ink2: "#52514e", line: "#3d3d3a", grid: "#c9c8c2",
    fills: {
      base: "#ffffff", gray: "#f3f3f1", blue: "#e8f1fc", aqua: "#e2f5ee", yellow: "#fdf3d9",
      violet: "#ecebfb", red: "#fde8e6", orange: "#fdeee6", dark: "#1f1f1d",
    },
    strokes: {
      base: "#3d3d3a", gray: "#8a8984", blue: "#2a78d6", aqua: "#1baf7a", yellow: "#c98500",
      violet: "#6250d6", red: "#e34948", orange: "#eb6834", dark: "#1f1f1d",
    },
  };
  const NS = "http://www.w3.org/2000/svg";
  const el = (tag, attrs, parent) => {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  };

  function text(parent, x, y, lines, opts = {}) {
    const size = opts.size || 14, sub = opts.subSize || 12.5, lh = opts.lh || 1.28;
    const boldFirst = opts.boldFirst !== false;
    const anchor = opts.anchor || "middle";
    const heights = lines.map((l, i) => (i === 0 && boldFirst ? size : sub) * lh);
    const total = heights.reduce((a, b) => a + b, 0);
    let cy = opts.top !== undefined ? opts.top : y - total / 2;
    lines.forEach((l, i) => {
      const fs = i === 0 && boldFirst ? size : sub;
      const t = el("text", {
        x, y: cy + heights[i] / 2, "text-anchor": anchor, "dominant-baseline": "central",
        "font-size": fs, "font-weight": i === 0 && boldFirst ? 700 : 400,
        fill: opts.color || (i === 0 && boldFirst ? C.ink : C.ink2),
        "font-style": opts.italic ? "italic" : "normal",
      }, parent);
      // **жирний** фрагмент усередині рядка
      const parts = l.split(/(\*\*[^*]+\*\*|`[^`]+`)/);
      parts.forEach((p) => {
        if (!p) return;
        const span = el("tspan", {}, t);
        if (p.startsWith("**")) { span.setAttribute("font-weight", 700); span.setAttribute("fill", C.ink); span.textContent = p.slice(2, -2); }
        else if (p.startsWith("`")) { span.setAttribute("font-family", "Menlo, monospace"); span.setAttribute("font-size", fs - 1); span.textContent = p.slice(1, -1); }
        else span.textContent = p;
      });
      cy += heights[i];
    });
  }

  function anchorOf(n, side, f = 0.5) {
    if (n.shape === "diamond" || n.shape === "circle" || n.shape === "init" || n.shape === "final") f = 0.5;
    switch (side) {
      case "l": return [n.x, n.y + n.h * f];
      case "r": return [n.x + n.w, n.y + n.h * f];
      case "t": return [n.x + n.w * f, n.y];
      case "b": return [n.x + n.w * f, n.y + n.h];
    }
  }

  function drawNode(svg, n) {
    const fill = C.fills[n.fill || "base"], stroke = C.strokes[n.fill || "base"];
    const g = el("g", {}, svg);
    const sw = n.strong ? 2 : 1.3;
    if (n.shape === "diamond") {
      const cx = n.x + n.w / 2, cy = n.y + n.h / 2;
      el("polygon", { points: `${cx},${n.y} ${n.x + n.w},${cy} ${cx},${n.y + n.h} ${n.x},${cy}`, fill, stroke, "stroke-width": sw }, g);
    } else if (n.shape === "db") {
      const ry = 9;
      el("path", {
        d: `M${n.x},${n.y + ry} A${n.w / 2},${ry} 0 0 1 ${n.x + n.w},${n.y + ry} V${n.y + n.h - ry} A${n.w / 2},${ry} 0 0 1 ${n.x},${n.y + n.h - ry} Z`,
        fill, stroke, "stroke-width": sw,
      }, g);
      el("path", { d: `M${n.x},${n.y + ry} A${n.w / 2},${ry} 0 0 0 ${n.x + n.w},${n.y + ry}`, fill: "none", stroke, "stroke-width": sw }, g);
    } else if (n.shape === "cloud") {
      el("rect", { x: n.x, y: n.y, width: n.w, height: n.h, rx: n.h / 2.6, fill, stroke, "stroke-width": sw, "stroke-dasharray": "0" }, g);
    } else if (n.shape === "doc") {
      const f = 14;
      el("path", { d: `M${n.x},${n.y} H${n.x + n.w - f} L${n.x + n.w},${n.y + f} V${n.y + n.h} H${n.x} Z`, fill, stroke, "stroke-width": sw }, g);
      el("path", { d: `M${n.x + n.w - f},${n.y} V${n.y + f} H${n.x + n.w}`, fill: "none", stroke, "stroke-width": sw }, g);
    } else if (n.shape === "pill") {
      el("rect", { x: n.x, y: n.y, width: n.w, height: n.h, rx: n.h / 2, fill, stroke, "stroke-width": sw }, g);
    } else if (n.shape === "init") {
      el("circle", { cx: n.x + n.w / 2, cy: n.y + n.h / 2, r: n.w / 2, fill: C.ink }, g);
      return;
    } else if (n.shape === "final") {
      el("circle", { cx: n.x + n.w / 2, cy: n.y + n.h / 2, r: n.w / 2, fill: "#fff", stroke: C.ink, "stroke-width": 1.5 }, g);
      el("circle", { cx: n.x + n.w / 2, cy: n.y + n.h / 2, r: n.w / 2 - 4, fill: C.ink }, g);
      return;
    } else if (n.shape === "entity") {
      const hh = 30, rh = 21;
      el("rect", { x: n.x, y: n.y, width: n.w, height: n.h, rx: 4, fill: "#fff", stroke, "stroke-width": sw }, g);
      el("path", { d: `M${n.x + 4},${n.y} H${n.x + n.w - 4} Q${n.x + n.w},${n.y} ${n.x + n.w},${n.y + 4} V${n.y + hh} H${n.x} V${n.y + 4} Q${n.x},${n.y} ${n.x + 4},${n.y} Z`, fill, stroke, "stroke-width": sw }, g);
      text(g, n.x + n.w / 2, n.y + hh / 2, [n.title], { size: 14 });
      n.fields.forEach((fl, i) => {
        const y = n.y + hh + 6 + rh * i + rh / 2;
        const [name, type, key] = fl;
        el("text", { x: n.x + 10, y, "dominant-baseline": "central", "font-size": 12.5, fill: C.ink, "font-weight": key ? 700 : 400 }, g).textContent = (key ? key + " " : "") + name;
        el("text", { x: n.x + n.w - 10, y, "dominant-baseline": "central", "text-anchor": "end", "font-size": 11.5, fill: C.ink2, "font-family": "Menlo, monospace" }, g).textContent = type;
      });
      return;
    } else {
      el("rect", { x: n.x, y: n.y, width: n.w, height: n.h, rx: n.rx ?? 6, fill, stroke, "stroke-width": sw, "stroke-dasharray": n.dashed ? "5 4" : "0" }, g);
    }
    const lines = n.text.split("\n");
    if (n.align === "left" && n.valign === "middle") text(g, n.x + 14, n.y + n.h / 2, lines, { anchor: "start", size: n.size, subSize: n.subSize, boldFirst: n.boldFirst });
    else if (n.align === "left") text(g, n.x + 12, 0, lines, { anchor: "start", top: n.y + 10, size: n.size, subSize: n.subSize, boldFirst: n.boldFirst });
    else text(g, n.x + n.w / 2, n.y + n.h / 2, lines, { size: n.size, subSize: n.subSize, boldFirst: n.boldFirst });
  }

  function route(a, b, fs, ts, via) {
    if (via && via.length) return [a, ...via, b];
    const H = (s) => s === "l" || s === "r";
    if (H(fs) && H(ts)) {
      if (Math.abs(a[1] - b[1]) < 0.5) return [a, b];
      const mx = (a[0] + b[0]) / 2;
      return [a, [mx, a[1]], [mx, b[1]], b];
    }
    if (!H(fs) && !H(ts)) {
      if (Math.abs(a[0] - b[0]) < 0.5) return [a, b];
      const my = (a[1] + b[1]) / 2;
      return [a, [a[0], my], [b[0], my], b];
    }
    if (H(fs)) return [a, [b[0], a[1]], b];
    return [a, [a[0], b[1]], b];
  }

  function labelBox(svg, x, y, lines, opts = {}) {
    const g = el("g", {}, svg);
    const size = opts.size || 12;
    const w = Math.max(...lines.map((l) => measure(l, size))) + 10;
    const h = lines.length * size * 1.25 + 6;
    el("rect", { x: x - w / 2, y: y - h / 2, width: w, height: h, fill: "#fff", rx: 3, opacity: 0.95 }, g);
    text(g, x, y, lines, { size, subSize: size, boldFirst: false, color: opts.color || C.ink2, italic: opts.italic });
  }

  let measurer;
  function measure(s, size) {
    if (!measurer) {
      measurer = el("text", { x: -1000, y: -1000 }, document.getElementById("svg"));
    }
    measurer.setAttribute("font-size", size);
    measurer.textContent = s.replace(/\*\*|`/g, "");
    return measurer.getComputedTextLength();
  }

  function drawEdge(svg, e, nodes) {
    const A = nodes[e.from], B = nodes[e.to];
    const a = e.a || anchorOf(A, e.fs || "r", e.fo);
    const b = e.b || anchorOf(B, e.ts || "l", e.to_ ?? e.tf);
    const pts = route(a, b, e.fs || "r", e.ts || "l", e.via);
    const d = pts.map((p, i) => (i ? "L" : "M") + p[0] + "," + p[1]).join(" ");
    const color = e.color ? C.strokes[e.color] : C.line;
    el("path", {
      d, fill: "none", stroke: color, "stroke-width": e.w || 1.4,
      "stroke-dasharray": e.dashed ? "6 4" : "0", "marker-end": e.noArrow ? "" : `url(#arr-${e.color || "base"})`,
      "marker-start": e.both ? `url(#arrs-${e.color || "base"})` : "",
      "stroke-linejoin": "round",
    }, svg);
    return () => {
    if (e.label) {
      let lx, ly;
      if (e.lx !== undefined) { lx = e.lx; ly = e.ly; }
      else {
        let best = 0, bi = 0;
        for (let i = 0; i < pts.length - 1; i++) {
          const L = Math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]);
          if (L > best) { best = L; bi = i; }
        }
        const t = e.lt ?? 0.5;
        lx = pts[bi][0] + (pts[bi + 1][0] - pts[bi][0]) * t;
        ly = pts[bi][1] + (pts[bi + 1][1] - pts[bi][1]) * t;
      }
      labelBox(svg, lx + (e.ldx || 0), ly + (e.ldy || 0), e.label.split("\n"), { size: e.lsize || 12, italic: e.litalic });
    }
    if (e.sl) labelBox(svg, a[0] + (e.slx || 0), a[1] + (e.sly || 0), [e.sl], { size: 12.5, color: C.ink });
    if (e.el) labelBox(svg, b[0] + (e.elx || 0), b[1] + (e.ely || 0), [e.el], { size: 12.5, color: C.ink });
    };
  }

  function markers(svg) {
    const defs = el("defs", {}, svg);
    for (const k of Object.keys(C.strokes)) {
      const m = el("marker", { id: `arr-${k}`, viewBox: "0 0 10 10", refX: 9.5, refY: 5, markerWidth: 8, markerHeight: 8, orient: "auto-start-reverse", markerUnits: "userSpaceOnUse" }, defs);
      el("path", { d: "M0,0.8 L10,5 L0,9.2 Z", fill: C.strokes[k] === "#3d3d3a" ? C.line : C.strokes[k] }, m);
      const m2 = el("marker", { id: `arrs-${k}`, viewBox: "0 0 10 10", refX: 9.5, refY: 5, markerWidth: 8, markerHeight: 8, orient: "auto-start-reverse", markerUnits: "userSpaceOnUse" }, defs);
      el("path", { d: "M0,0.8 L10,5 L0,9.2 Z", fill: C.strokes[k] === "#3d3d3a" ? C.line : C.strokes[k] }, m2);
    }
  }

  function drawGraph(spec) {
    const svg = document.getElementById("svg");
    svg.setAttribute("width", spec.width);
    svg.setAttribute("height", spec.height);
    svg.setAttribute("viewBox", `0 0 ${spec.width} ${spec.height}`);
    markers(svg);
    (spec.groups || []).forEach((g) => {
      el("rect", { x: g.x, y: g.y, width: g.w, height: g.h, rx: 10, fill: C.fills[g.fill || "gray"], stroke: C.strokes[g.fill || "gray"], "stroke-width": 1.1, "stroke-dasharray": g.solid ? "0" : "6 4", opacity: 1 }, svg);
      el("text", { x: g.labelRight ? g.x + g.w - 12 : g.x + 12, y: g.labelBottom ? g.y + g.h - 9 : g.y + 18, "text-anchor": g.labelRight ? "end" : "start", "font-size": 13, "font-weight": 700, fill: C.strokes[g.fill || "gray"] === "#8a8984" ? C.ink2 : C.strokes[g.fill || "gray"] }, svg).textContent = g.label;
    });
    const nodes = {};
    (spec.nodes || []).forEach((n) => {
      if (n.shape === "entity") n.h = 30 + 12 + 21 * n.fields.length;
      nodes[n.id] = n;
    });
    const later = (spec.edges || []).map((e) => drawEdge(svg, e, nodes));
    (spec.nodes || []).forEach((n) => drawNode(svg, n));
    later.forEach((f) => f());
    (spec.labels || []).forEach((l) => labelBox(svg, l.x, l.y, l.text.split("\n"), { size: l.size || 12, italic: l.italic, color: l.color }));
    (spec.texts || []).forEach((t) => text(svg, t.x, t.y, t.text.split("\n"), { anchor: t.anchor || "start", size: t.size || 13, subSize: t.subSize || 12.5, boldFirst: t.bold !== false, color: t.color }));
  }

  // ---------------- діаграма послідовності
  function drawSeq(spec) {
    const svg = document.getElementById("svg");
    svg.setAttribute("width", spec.width);
    svg.setAttribute("height", spec.height);
    svg.setAttribute("viewBox", `0 0 ${spec.width} ${spec.height}`);
    markers(svg);
    const P = spec.participants, n = P.length;
    const left = spec.left || 70, right = spec.width - (spec.right || 70);
    const step = (right - left) / (n - 1);
    const xs = {};
    P.forEach((p, i) => (xs[p.id] = left + i * step));
    const top = 16, ph = 50, pw = Math.min(step - 10, 128);
    // фрейми
    (spec.frames || []).forEach((f) => {
      const x1 = xs[f.from] - pw / 2 - 6, x2 = xs[f.to] + pw / 2 + 6;
      el("rect", { x: x1, y: f.y1, width: x2 - x1, height: f.y2 - f.y1, fill: C.fills[f.fill || "yellow"], stroke: C.strokes[f.fill || "yellow"], "stroke-width": 1, rx: 4, opacity: 0.55 }, svg);
      const tw = measure(f.label, 12) + 14;
      el("path", { d: `M${x1},${f.y1} h${tw} v14 l-6,6 H${x1} Z`, fill: "#fff", stroke: C.strokes[f.fill || "yellow"], "stroke-width": 1 }, svg);
      el("text", { x: x1 + 6, y: f.y1 + 11, "font-size": 12, "font-weight": 700, fill: C.ink }, svg).textContent = f.label;
      (f.dividers || []).forEach((d) => {
        el("line", { x1, x2, y1: d.y, y2: d.y, stroke: C.strokes[f.fill || "yellow"], "stroke-dasharray": "6 4" }, svg);
        el("text", { x: x1 + 8, y: d.y + 14, "font-size": 12, fill: C.ink2, "font-style": "italic" }, svg).textContent = d.label;
      });
    });
    P.forEach((p) => {
      el("line", { x1: xs[p.id], x2: xs[p.id], y1: top + ph, y2: spec.height - 10, stroke: C.grid, "stroke-width": 1.2, "stroke-dasharray": "5 4" }, svg);
    });
    P.forEach((p) => {
      const n2 = { x: xs[p.id] - pw / 2, y: top, w: pw, h: ph, text: p.text, fill: p.fill || "gray", size: 12.5, subSize: 11.5 };
      drawNode(svg, n2);
    });
    (spec.sections || []).forEach((s) => {
      el("line", { x1: 8, x2: spec.width - 8, y1: s.y, y2: s.y, stroke: C.ink2, "stroke-width": 1 }, svg);
      const w = measure(s.label, 12.5) + 18;
      el("rect", { x: spec.width / 2 - w / 2, y: s.y - 11, width: w, height: 22, fill: "#fff", stroke: C.ink2, rx: 3 }, svg);
      el("text", { x: spec.width / 2, y: s.y + 1, "text-anchor": "middle", "dominant-baseline": "central", "font-size": 12.5, "font-weight": 700, fill: C.ink }, svg).textContent = s.label;
    });
    spec.messages.forEach((m) => {
      const x1 = xs[m.from], x2 = xs[m.to], y = m.y;
      const color = m.color ? C.strokes[m.color] : C.line;
      if (m.from === m.to) {
        el("path", { d: `M${x1},${y} h34 v18 h-30`, fill: "none", stroke: color, "stroke-width": 1.3, "marker-end": `url(#arr-${m.color || "base"})` }, svg);
        text(svg, x1 + 40, y + 9, m.text.split("\n"), { anchor: "start", size: 12, subSize: 12, boldFirst: false, color: C.ink });
        return;
      }
      el("line", { x1, x2: x2 + (x2 > x1 ? -1 : 1), y1: y, y2: y, stroke: color, "stroke-width": 1.3, "stroke-dasharray": m.ret ? "6 4" : "0", "marker-end": `url(#arr-${m.color || "base"})` }, svg);
      const lines = m.text.split("\n");
      const mx = (x1 + x2) / 2;
      lines.reverse().forEach((l, i) => {
        const t = el("text", { x: mx, y: y - 5 - i * 14.5, "text-anchor": "middle", "font-size": 12, fill: m.ret ? C.ink2 : C.ink }, svg);
        t.textContent = l;
      });
    });
    (spec.notes || []).forEach((nt) => {
      const n2 = { x: nt.x, y: nt.y, w: nt.w, h: nt.h, text: nt.text, fill: nt.fill || "yellow", size: 12, subSize: 11.5, boldFirst: nt.bold };
      drawNode(svg, n2);
    });
  }

  window.renderDiagram = (spec) => (spec.kind === "seq" ? drawSeq(spec) : drawGraph(spec));
})();
