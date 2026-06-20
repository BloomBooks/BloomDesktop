"""Generate a self-contained HTML preview of every Bloom game theme.

Parses gamesThemes.less, resolves the full --game-* CSS variable cascade for each
theme to concrete colors, measures WCAG contrast for every meaningful element
pair, and writes one scrollable HTML page. Testers can scroll through and see every
element of every theme rendered in its real colors, with any low-contrast pair
flagged.

Each failing card is outlined orange (weak) or red (bad) and offers one or two
pre-computed alternative colors as checkboxes. An operator ticks the ones they
approve and clicks "Copy approved changes" to put an apply-ready block on the
clipboard, which can be handed back to an agent to edit gamesThemes.less.

Usage:
    py generate_preview.py [output.html] [path/to/gamesThemes.less]
"""

import colorsys
import os
import re
import sys

from contrast import parse, contrast, luminance, rating

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEFAULT_LESS = os.path.join(
    REPO, "src", "content", "templates", "template books", "Games", "gamesThemes.less"
)


# ---------------------------------------------------------------------------
# Parsing the LESS into per-theme variable maps
# ---------------------------------------------------------------------------

def _strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def _vars_in(block):
    """Ordered list of (name, value) for each `--name: value;` in a block."""
    return [(m.group(1).strip(), m.group(2).strip())
            for m in re.finditer(r"(--[\w-]+)\s*:\s*([^;]+);", block)]


def parse_themes(less_text):
    """Return (theme_name -> {var: rawvalue}) with the full cascade merged in.

    Cascade order applied (later wins): base literals, the apply-game-theme()
    mixin, then the theme's own overrides - matching how the CSS resolves.
    """
    text = _strip_comments(less_text)

    mixin = re.search(r"\.apply-game-theme\(\)\s*\{(.*?)\n\}", text, re.S)
    mixin_vars = _vars_in(mixin.group(1)) if mixin else []

    base = re.search(r'\.bloom-page\[class\*="game-theme-"\]\s*\{(.*?)\n\}', text, re.S)
    base_vars = _vars_in(base.group(1)) if base else []

    themes = {}
    for m in re.finditer(r"\.bloom-page\.game-theme-([\w-]+)\s*\{(.*?)\n\}", text, re.S):
        name = m.group(1)
        merged = {}
        for k, v in base_vars + mixin_vars + _vars_in(m.group(2)):
            merged[k] = v
        themes[name] = merged
    return themes


def resolve(name, vars_map, _seen=None):
    """Resolve a variable to a concrete color string, following var() chains."""
    _seen = _seen or set()
    if name in _seen:
        raise ValueError(f"variable cycle at {name}")
    _seen = _seen | {name}
    val = vars_map.get(name)
    if val is None:
        return None
    m = re.fullmatch(r"var\(\s*(--[\w-]+)\s*\)", val)
    if m:
        return resolve(m.group(1), vars_map, _seen)
    return val  # a literal color (hex / named)


# ---------------------------------------------------------------------------
# Contrast checks per theme
# ---------------------------------------------------------------------------

def _ratio(fg, bg):
    return contrast(fg, parse(bg))


def _hex(rgb):
    return "#%02x%02x%02x" % rgb


def eval_check(ck, g):
    """Re-evaluate a check's contrast ratio under a (possibly overridden) resolver g.

    Mirrors how the ratio was first computed in build(): a 'visible' check is the
    better of fill-vs-page and outline-vs-page; a 'simple' check is fg-vs-bg.
    """
    if ck["check_kind"] == "visible":
        bg = g(ck["bg_var"])
        return max(_ratio(g(ck["fix_var"]), bg), _ratio(g(ck["outline_var"]), bg))
    return _ratio(g(ck["fix_var"]), g(ck["bg_var"]))


def _apply(vars_map, changes):
    """vars_map with a list of (var, value) overrides applied."""
    vm = dict(vars_map)
    for var, value in changes:
        vm[var] = value
    return vm


def _valid(comp, vm2, buffer=0.3):
    """True if EVERY check in the component clears its threshold under overrides vm2.

    The heart of correctness: a candidate is only valid if it keeps *all* of an
    element's contrast relationships healthy at once - e.g. recoloring a draggable's
    fill must keep both 'fill vs page' AND 'text on fill' passing."""
    g2 = lambda n: resolve(n, vm2)
    return all(eval_check(c, g2) >= c["threshold"] + buffer for c in comp["checks"])


def _option(changes, label, comp, vars_map, coord=False, why=None, recommended=False):
    """Package a candidate (one or more var changes) with before/after ratios.

    `results` records every relationship the change touches, so the operator sees the
    full trade-off - including a relationship that got *worse* but still passes.
    `recommended`/`why` mark an AI-curated pick (chosen to fit the whole theme) so the
    UI can surface it ahead of the mechanically-generated alternatives."""
    vm2 = _apply(vars_map, changes)
    g0, g2 = (lambda n: resolve(n, vars_map)), (lambda n: resolve(n, vm2))
    results = [{"label": c["label"], "before": eval_check(c, g0),
                "after": eval_check(c, g2), "threshold": c["threshold"]}
               for c in comp["checks"]]
    score = min(r["after"] / r["threshold"] for r in results)  # binding margin; higher better
    return {"changes": [{"var": v, "value": val} for v, val in changes],
            "label": label, "results": results, "score": score, "coord": coord,
            "why": why, "recommended": recommended}


def curated_options(name, comp, ck, vars_map, curated):
    """Validated AI-curated fixes for this check, from the curated suggestions file.

    Each is held to the same whole-component validation as a generated candidate, so a
    hand-picked color that would break a relationship is dropped rather than shipped."""
    key = f"{comp['title']}|{ck['label']}"
    out = []
    for entry in curated.get(name, {}).get(key, []):
        changes = [(v, val) for v, val in entry["changes"]]
        if _valid(comp, _apply(vars_map, changes)):
            out.append(_option(changes, entry["label"], comp, vars_map,
                               why=entry.get("why"), recommended=True))
    return out


def _search_one(var, comp, vars_map):
    """Smallest same-hue lightness move of `var` that makes the WHOLE component valid."""
    cur = resolve(var, vars_map)
    r, g, b = parse(cur)
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    best = None
    for sign in (-1, 1):
        for step in range(1, 101):
            l2 = l + sign * step / 100
            if l2 < 0 or l2 > 1:
                break
            rr, gg, bb = colorsys.hls_to_rgb(h, l2, s)
            val = _hex((round(rr * 255), round(gg * 255), round(bb * 255)))
            if _valid(comp, _apply(vars_map, [(var, val)])):
                if best is None or step < best[1]:
                    best = (val, step, "darken" if sign < 0 else "lighten")
                break
    return best


def candidates(ck, comp, vars_map):
    """Whole-component-valid fixes for a failing check (at most two, best first).

    Knobs depend on the check: a 'visible' check (is the shape distinct from the page?)
    can be fixed by recoloring the fill OR the outline; a 'simple' fg/bg check can adjust
    its foreground or its background fill - never the shared page background. Plus, when
    making a text-bearing element stand out from the page, a single-color nudge muddies
    the fill and starves its text, so we also offer a coordinated 'darker/lighter fill +
    opposite-tone text' fix that keeps both relationships strong. Every candidate is
    validated against all of the component's checks.
    """
    page_dark = luminance(parse(resolve("--game-page-bg-color", vars_map))) < 0.5

    if ck["check_kind"] == "visible":
        knobs = [(ck["fix_var"], "fill"), (ck["outline_var"], "outline")]
    else:
        knobs = [(ck["fix_var"], "color")]
        if not ck["bg_is_page"]:
            knobs.append((ck["bg_var"], "background"))

    opts = []
    for var, role in knobs:
        cur = resolve(var, vars_map)
        adj = _search_one(var, comp, vars_map)
        if adj:
            opts.append(_option([(var, adj[0])], f"{adj[2]} the {role}", comp, vars_map))
        for prole, pcol in ck["palette"]:  # one on-brand pick per knob, if it validates
            if pcol.lower() != cur.lower() and _valid(comp, _apply(vars_map, [(var, pcol)])):
                opts.append(_option([(var, pcol)], f"use {prole}", comp, vars_map))
                break

    # Coordinated fill+text fix for "element vs page" on a text-bearing element.
    if ck["bg_is_page"]:
        fill_var = ck["fix_var"]
        text_sib = next((c for c in comp["checks"]
                         if c is not ck and c["check_kind"] == "simple"
                         and c["bg_var"] == fill_var), None)
        if text_sib:
            text_var = text_sib["fix_var"]
            new_text = "black" if page_dark else "white"
            adj = _search_one(fill_var, comp, _apply(vars_map, [(text_var, new_text)]))
            if adj:
                opts.append(_option([(fill_var, adj[0]), (text_var, new_text)],
                                    f"{adj[2]} fill + {new_text} text", comp, vars_map, coord=True))

    # dedupe by change-set
    uniq, seen = [], set()
    for o in opts:
        key = tuple(sorted((c["var"], c["value"].lower()) for c in o["changes"]))
        if key not in seen:
            seen.add(key)
            uniq.append(o)

    # Prefer to surface one coordinated (highest-quality) fix plus the best simple one.
    coord = sorted((o for o in uniq if o["coord"]), key=lambda o: -o["score"])
    simple = sorted((o for o in uniq if not o["coord"]), key=lambda o: -o["score"])
    out = (coord[:1] + simple)[:2] if coord else simple[:2]
    return out


# Each component: (heading, kind, checks). A check is a dict; `fix_var` is the
# theme variable an operator would change to fix it (always the foreground).
def build(theme):
    g = lambda n: resolve(n, theme)

    palette = []
    seen_pal = set()
    for role, var in [("text color", "--game-text-color"),
                      ("primary color", "--game-primary-color"),
                      ("secondary color", "--game-secondary-color"),
                      ("header text", "--game-header-color"),
                      ("header bg", "--game-header-bg-color"),
                      ("button text", "--game-button-text-color")]:
        c = g(var)
        if c and c.lower() not in seen_pal:
            palette.append((role, c))
            seen_pal.add(c.lower())

    def chk(label, fg_var, bg_var, threshold):
        fg, bg = g(fg_var), g(bg_var)
        r = _ratio(fg, bg)
        return {"label": label, "check_kind": "simple",
                "fix_var": fg_var, "bg_var": bg_var, "outline_var": None,
                "bg_is_page": bg_var == "--game-page-bg-color",
                "fg": fg, "bg": bg, "ratio": r,
                "threshold": threshold, "pass": r >= threshold,
                "rate": rating(r, threshold), "palette": palette}

    def chk_visible(label, fill_var, outline_var, bg_var, threshold):
        """A filled control is 'visible' if EITHER its fill or its outline contrasts
        with the page. (White-on-white buttons are fine when they have a colored
        border, so checking the fill alone would cry wolf.) Visibility is judged
        against the page, so the fill or the outline can be recolored - never the page."""
        bg = g(bg_var)
        r = max(_ratio(g(fill_var), bg), _ratio(g(outline_var), bg))
        return {"label": label, "check_kind": "visible",
                "fix_var": fill_var, "bg_var": bg_var, "outline_var": outline_var,
                "bg_is_page": True,
                "fg": g(fill_var), "bg": bg, "ratio": r,
                "threshold": threshold, "pass": r >= threshold,
                "rate": rating(r, threshold), "palette": palette}

    components = [
        {"title": "Page text", "kind": "page", "checks": [
            chk("body text on page", "--game-text-color", "--game-page-bg-color", 4.5),
            chk("page number on page", "--game-page-number-color", "--game-page-bg-color", 4.5)]},
        {"title": "Header bar", "kind": "header", "checks": [
            chk("header text on header", "--game-header-color", "--game-header-bg-color", 4.5)]},
        {"title": "Draggable item", "kind": "draggable", "checks": [
            chk("text on draggable", "--game-draggable-color", "--game-draggable-bg-color", 4.5),
            chk("draggable vs page", "--game-draggable-bg-color", "--game-page-bg-color", 3.0)]},
        {"title": "Empty drop target", "kind": "target", "checks": [
            chk("dashed outline vs page", "--game-draggable-target-outline-color", "--game-page-bg-color", 3.0)]},
        {"title": "Answer button", "kind": "button", "checks": [
            chk("button text on button", "--game-button-text-color", "--game-button-bg-color", 4.5),
            chk_visible("button visible vs page", "--game-button-bg-color", "--game-button-outline-color", "--game-page-bg-color", 3.0)]},
        {"title": "Correct / wrong", "kind": "verdict", "checks": [
            chk("text on correct", "--game-button-correct-color", "--game-button-correct-bg-color", 4.5),
            chk("text on wrong", "--game-button-wrong-color", "--game-button-wrong-bg-color", 4.5)]},
        {"title": "Control button (Check)", "kind": "control", "checks": [
            chk("control button vs page", "--game-control-button-bg-color", "--game-page-bg-color", 3.0)]},
        {"title": "Checkbox", "kind": "checkbox", "checks": [
            chk("checkbox outline vs page", "--game-checkbox-outline-color", "--game-page-bg-color", 3.0),
            chk("checkbox text vs page", "--game-checkbox-text-color", "--game-page-bg-color", 4.5)]},
        {"title": "Selected checkbox", "kind": "checkbox-sel", "checks": [
            chk("text on selected", "--game-selected-checkbox-color", "--game-selected-checkbox-bg-color", 4.5),
            chk_visible("selected visible vs page", "--game-selected-checkbox-bg-color", "--game-selected-checkbox-outline-color", "--game-page-bg-color", 3.0)]},
    ]
    return g("--game-page-bg-color"), components


# ---------------------------------------------------------------------------
# HTML rendering
# ---------------------------------------------------------------------------

def _swatch_html(kind, g):
    page = g("--game-page-bg-color")
    if kind == "page":
        return (f'<div class="mock" style="background:{page};color:{g("--game-text-color")}">'
                f'Aa Bb Cc <span style="color:{g("--game-page-number-color")};font-weight:700">12</span></div>')
    if kind == "header":
        return (f'<div class="mock" style="background:{g("--game-header-bg-color")};'
                f'color:{g("--game-header-color")};font-weight:700">My Game</div>')
    if kind == "draggable":
        return (f'<div class="mock" style="background:{page};justify-content:center">'
                f'<span class="chip" style="background:{g("--game-draggable-bg-color")};'
                f'color:{g("--game-draggable-color")}">cat</span></div>')
    if kind == "target":
        return (f'<div class="mock" style="background:{page};justify-content:center">'
                f'<span class="tgt" style="border:2px dashed {g("--game-draggable-target-outline-color")}"></span></div>')
    if kind == "button":
        return (f'<div class="mock" style="background:{page};justify-content:center">'
                f'<span class="btn" style="background:{g("--game-button-bg-color")};'
                f'color:{g("--game-button-text-color")};border:2px solid {g("--game-button-outline-color")}">cat</span></div>')
    if kind == "verdict":
        return (f'<div class="mock" style="background:{page};gap:8px;justify-content:center">'
                f'<span class="btn" style="background:{g("--game-button-correct-bg-color")};color:{g("--game-button-correct-color")}">right</span>'
                f'<span class="btn" style="background:{g("--game-button-wrong-bg-color")};color:{g("--game-button-wrong-color")}">wrong</span></div>')
    if kind == "control":
        return (f'<div class="mock" style="background:{page};justify-content:center">'
                f'<span class="btn" style="background:{g("--game-control-button-bg-color")};'
                f'color:{g("--game-control-button-color")}">Check</span></div>')
    if kind == "checkbox":
        return (f'<div class="mock" style="background:{page};align-items:center;gap:8px;justify-content:center">'
                f'<span class="cbx" style="border:2px solid {g("--game-checkbox-outline-color")}"></span>'
                f'<span style="color:{g("--game-checkbox-text-color")}">option</span></div>')
    if kind == "checkbox-sel":
        return (f'<div class="mock" style="background:{page};align-items:center;gap:8px;justify-content:center">'
                f'<span class="cbx" style="background:{g("--game-selected-checkbox-bg-color")};'
                f'color:{g("--game-selected-checkbox-color")};border:2px solid {g("--game-selected-checkbox-outline-color")};'
                f'display:flex;align-items:center;justify-content:center;font-size:13px">&#10003;</span>'
                f'<span style="color:{g("--game-checkbox-text-color")}">option</span></div>')
    return ""


def _esc(s):
    return s.replace("&", "&amp;").replace('"', "&quot;").replace("<", "&lt;")


def render(themes, curated=None):
    curated = curated or {}
    nav = []
    sections = ""
    total_issues = 0

    for name, vars_map in themes.items():
        g = lambda n, vm=vars_map: resolve(n, vm)
        page_bg, components = build(vars_map)

        theme_issues = 0
        cards_html = ""
        for comp in components:
            failing = [ck for ck in comp["checks"] if not ck["pass"]]
            theme_issues += len(failing)
            severity = ""
            if failing:
                severity = "bad" if any(ck["ratio"] < 2.0 for ck in failing) else "weak"

            checks_html = ""
            for ck in comp["checks"]:
                cls = "ok" if ck["pass"] else ("weak" if ck["ratio"] >= 2 else "fail")
                checks_html += (
                    f'<div class="check {cls}"><span class="ck-label">{ck["label"]}</span>'
                    f'<span class="ck-ratio">{ck["ratio"]:.2f}:1</span>'
                    f'<span class="ck-need">need {ck["threshold"]:.1f}</span></div>')

            fixes_html = ""
            for ck in failing:
                # AI-curated recommendation(s) first, then de-duplicated generated fallbacks.
                rec = curated_options(name, comp, ck, vars_map, curated)
                rec_keys = {tuple(sorted((c["var"], c["value"].lower()) for c in o["changes"])) for o in rec}
                gen = [o for o in candidates(ck, comp, vars_map)
                       if tuple(sorted((c["var"], c["value"].lower()) for c in o["changes"])) not in rec_keys]
                opts = rec + gen
                if not opts:
                    fixes_html += (f'<div class="fixrow"><div class="fixhdr">Fix for '
                                   f'<b>{ck["label"]}</b> &mdash; no single-color fix keeps every '
                                   f'relationship on this element passing; needs a manual redesign</div></div>')
                    continue
                group = f"{name}|{comp['title']}|{ck['label']}"
                opt_html = ""
                for opt in opts:
                    note = f'{name}: fixes {ck["label"]}'
                    # Render the same element with this candidate's change(s) substituted,
                    # so the operator sees the fix in context. Override at the variable level
                    # so dependent variables (e.g. a selected-checkbox outline that follows
                    # the fill) update too.
                    vm2 = _apply(vars_map, [(c["var"], c["value"]) for c in opt["changes"]])
                    preview = _swatch_html(comp["kind"], lambda n, vm=vm2: resolve(n, vm))
                    # Show before->after for EVERY relationship on this element. A value that
                    # only just passes (or got worse but still passes) is amber, not green,
                    # so a barely-passing 5.40 is never dressed up as "high contrast".
                    res_html = ""
                    for r in opt["results"]:
                        comfortable = r["after"] >= 1.6 * r["threshold"]
                        cls = "ok" if comfortable else "warn"
                        res_html += (f'<span class="mini {cls}">{r["label"]} '
                                     f'{r["before"]:.1f}&rarr;{r["after"]:.1f}</span>')
                    dot = opt["changes"][0]["value"]
                    swatches = " ".join(c["value"] for c in opt["changes"])
                    var_html = "".join(f'<code>{c["var"]}: {c["value"]}</code>' for c in opt["changes"])
                    data_changes = ";;".join(f'{c["var"]}|{c["value"]}' for c in opt["changes"])
                    rec_cls = " rec" if opt.get("recommended") else ""
                    rec_tag = '<span class="rectag">Recommended</span>' if opt.get("recommended") else ""
                    why_html = (f'<div class="why">{opt["why"]}</div>'
                                if opt.get("why") else "")
                    opt_html += (
                        f'<label class="opt{rec_cls}"><div class="opt-body">'
                        f'<div class="opt-meta"><input type="checkbox" class="fix-opt" '
                        f'data-group="{_esc(group)}" data-theme="{name}" '
                        f'data-changes="{_esc(data_changes)}" data-note="{_esc(note)}">'
                        f'<span class="dot" style="background:{dot}"></span>'
                        f'<span class="opt-label">{opt["label"]}</span>{rec_tag}</div>'
                        f'{why_html}'
                        f'<div class="opt-var">{var_html}</div>'
                        f'<div class="opt-checks">{res_html}</div>'
                        f'{preview}</div></label>')
                fixes_html += (f'<div class="fixrow"><div class="fixhdr">Fix for '
                               f'<b>{ck["label"]}</b></div>{opt_html}</div>')

            flagged = "flagged" if failing else ""
            cap = '<div class="cap">current</div>' if failing else ""
            cards_html += (
                f'<div class="card {severity} {flagged}">'
                f'<div class="card-title">{comp["title"]}</div>'
                f'{_swatch_html(comp["kind"], g)}{cap}'
                f'<div class="checks">{checks_html}</div>'
                f'{("<div class=fixes>" + fixes_html + "</div>") if fixes_html else ""}'
                f'</div>')

        total_issues += theme_issues
        nav.append((name, theme_issues))
        badge = ('<span class="sec-badge clean">all pass</span>' if theme_issues == 0
                 else f'<span class="sec-badge dirty">{theme_issues} low-contrast</span>')
        sections += (f'<section id="{name}"><div class="sec-head" style="--pg:{page_bg}">'
                     f'<h2>{name}</h2>{badge}</div><div class="grid">{cards_html}</div></section>')

    nav_html = "".join(
        f'<a href="#{n}" class="navlink {"clean" if i == 0 else "dirty"}">{n}'
        f'<span class="navbadge">{"✓" if i == 0 else i}</span></a>'
        for n, i in nav)
    summary = ("Every theme passes." if total_issues == 0
               else f"{total_issues} element pairs fall below threshold across "
                    f"{sum(1 for _, i in nav if i)} theme(s). Tick the fixes you approve, then Copy.")
    return PAGE.replace("{{NAV}}", nav_html).replace("{{SECTIONS}}", sections).replace("{{SUMMARY}}", summary)


PAGE = r"""<title>Bloom Game Theme Preview</title>
<style>
  :root{--ink:#1c1c1e;--muted:#6e6e73;--line:#e5e5ea;--bg:#f5f5f7;--good:#1f8a4c;
        --warnc:#c07700;--failc:#c2241b;--warnb:#f3a712;--failb:#e23b2e;}
  *{box-sizing:border-box}
  body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;color:var(--ink);background:var(--bg);margin:0;padding-bottom:80px;}
  header.top{position:sticky;top:0;z-index:10;background:rgba(255,255,255,.92);backdrop-filter:blur(8px);
    border-bottom:1px solid var(--line);padding:14px 24px;}
  header.top h1{font-size:18px;margin:0 0 8px;}
  header.top p{margin:0 0 10px;color:var(--muted);font-size:13px;}
  header.top p.std{font-size:12px;}
  .stdtag{display:inline-block;background:#eef3fd;color:#1c5fc7;font-weight:700;
    padding:2px 9px;border-radius:6px;margin-right:6px;}
  .nav{display:flex;flex-wrap:wrap;gap:8px;}
  .navlink{display:inline-flex;align-items:center;gap:6px;text-decoration:none;font-size:12.5px;font-weight:600;
    padding:4px 10px;border-radius:20px;border:1px solid var(--line);color:var(--ink);background:#fff;}
  .navlink.dirty{border-color:#f2c9c4;background:#fdecec;color:var(--failc);}
  .navlink.clean{color:var(--good);}
  .navbadge{font-size:11px;min-width:16px;text-align:center;}
  main{max-width:1120px;margin:0 auto;padding:24px;}
  section{margin-bottom:40px;scroll-margin-top:120px;}
  .sec-head{display:flex;align-items:center;gap:12px;padding:12px 16px;border-radius:10px;margin-bottom:14px;
    background:var(--pg);border:1px solid var(--line);}
  .sec-head h2{font-size:20px;margin:0;mix-blend-mode:difference;color:#fff;}
  .sec-badge{font-size:12px;font-weight:700;padding:3px 10px;border-radius:20px;}
  .sec-badge.clean{background:#e7f5ec;color:var(--good);}
  .sec-badge.dirty{background:#fdecec;color:var(--failc);}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(244px,1fr));gap:14px;align-items:start;}
  .card{background:#fff;border:1px solid var(--line);border-radius:12px;padding:14px;}
  .card.weak{border:3px solid var(--warnb);}
  .card.bad{border:3px solid var(--failb);}
  .card.addressed{border:3px solid var(--good);box-shadow:0 0 0 1px var(--good) inset;}
  .card-title{font-size:13px;font-weight:700;margin-bottom:10px;color:var(--ink);}
  .mock{border-radius:8px;padding:14px 12px;min-height:58px;display:flex;align-items:center;font-size:15px;gap:8px;}
  .chip{padding:6px 14px;border-radius:8px;font-weight:700;font-size:14px;}
  .tgt{width:80px;height:40px;border-radius:6px;display:inline-block;}
  .btn{padding:6px 14px;border-radius:8px;font-weight:700;font-size:13px;}
  .cbx{width:22px;height:22px;border-radius:5px;display:inline-block;}
  .cap{font-size:10px;text-transform:uppercase;letter-spacing:.5px;color:var(--muted);margin-top:4px;}
  .checks{margin-top:12px;display:flex;flex-direction:column;gap:5px;}
  .check{display:grid;grid-template-columns:1fr auto auto;gap:8px;align-items:center;font-size:11.5px;padding:4px 8px;border-radius:6px;}
  .check.ok{background:#f1f8f3;} .check.weak{background:#fff6e9;} .check.fail{background:#fdeeed;}
  .ck-label{color:var(--ink);} .ck-ratio{font-variant-numeric:tabular-nums;font-weight:700;}
  .check.ok .ck-ratio{color:var(--good);} .check.weak .ck-ratio{color:var(--warnc);} .check.fail .ck-ratio{color:var(--failc);}
  .ck-need{color:var(--muted);font-size:10.5px;}
  .fixes{margin-top:12px;border-top:1px dashed var(--line);padding-top:10px;display:flex;flex-direction:column;gap:10px;}
  .fixhdr{font-size:11px;color:var(--muted);margin-bottom:6px;}
  .fixhdr code{font-size:10.5px;background:#f0f0f3;padding:1px 4px;border-radius:4px;}
  .opt{display:block;font-size:11.5px;cursor:pointer;padding:6px;border-radius:8px;border:1px solid var(--line);margin-bottom:8px;}
  .opt:hover{background:#f7f7fa;}
  .opt.rec{border-color:#1c6feb;background:#f3f8ff;}
  .rectag{font-size:9px;font-weight:800;letter-spacing:.4px;text-transform:uppercase;color:#fff;
    background:#1c6feb;padding:1px 6px;border-radius:10px;}
  .why{font-size:11px;color:#33405a;line-height:1.45;}
  .opt-body{display:flex;flex-direction:column;gap:6px;}
  .opt-meta{display:flex;align-items:center;gap:7px;}
  .opt-meta input{margin:0;cursor:pointer;}
  .dot{width:14px;height:14px;border-radius:4px;border:1px solid rgba(0,0,0,.2);flex:none;}
  .opt-label{flex:1;} .opt-ratio{font-weight:700;color:var(--good);font-variant-numeric:tabular-nums;}
  .opt-var{display:flex;flex-direction:column;gap:3px;}
  .opt-var code{font-size:10px;color:var(--muted);background:#f4f4f6;padding:1px 5px;border-radius:4px;word-break:break-all;}
  .opt-checks{display:flex;flex-wrap:wrap;gap:4px;}
  .mini{font-size:10px;padding:1px 6px;border-radius:10px;font-variant-numeric:tabular-nums;}
  .mini.ok{background:#e7f5ec;color:var(--good);}
  .mini.warn{background:#fff4e5;color:var(--warnc);}
  .opt .mock{min-height:42px;padding:9px 10px;font-size:13px;}
  .opt .chip,.opt .btn{padding:4px 11px;font-size:12px;}
  .opt .tgt{width:64px;height:28px;} .opt .cbx{width:18px;height:18px;}
  .opt:has(input:checked){border-color:var(--good);background:#f1f8f3;}
  .bar{position:fixed;bottom:0;left:0;right:0;background:#fff;border-top:1px solid var(--line);
    padding:12px 24px;display:flex;align-items:center;gap:16px;z-index:20;box-shadow:0 -2px 10px rgba(0,0,0,.05);}
  .bar .count{font-size:13px;color:var(--muted);} .bar .count b{color:var(--ink);}
  .copybtn{margin-left:auto;background:#1c6feb;color:#fff;border:none;border-radius:8px;padding:10px 18px;
    font-size:14px;font-weight:700;cursor:pointer;} .copybtn:disabled{background:#b9c6d8;cursor:default;}
  .toast{position:fixed;bottom:74px;left:50%;transform:translateX(-50%);background:#1c1c1e;color:#fff;
    padding:9px 16px;border-radius:8px;font-size:13px;opacity:0;transition:opacity .2s;pointer-events:none;z-index:50;}
  .toast.show{opacity:1;}
  .modal{display:none;position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:40;align-items:center;justify-content:center;padding:20px;}
  .modal.show{display:flex;}
  .modal-box{background:#fff;border-radius:12px;padding:16px;width:min(700px,94vw);max-height:82vh;display:flex;flex-direction:column;gap:10px;}
  .modal-hd{display:flex;justify-content:space-between;align-items:center;font-weight:700;}
  .modal-hd button{border:1px solid var(--line);background:#fff;border-radius:6px;padding:4px 12px;cursor:pointer;font-size:13px;}
  .modal-tip{margin:0;font-size:12px;color:var(--muted);}
  #out{width:100%;flex:1;min-height:260px;font-family:ui-monospace,Consolas,monospace;font-size:12px;
    border:1px solid var(--line);border-radius:8px;padding:10px;resize:vertical;background:#fbfbfd;}
</style>
<header class="top">
  <h1>Bloom game theme preview &mdash; every element, every theme</h1>
  <p>Each card renders a real element in its resolved colors; badges are WCAG contrast ratios.
     Cards outlined <b style="color:var(--warnc)">orange</b> are weak,
     <b style="color:var(--failc)">red</b> are bad. Tick an alternative to approve it
     (cards turn green), then <b>Copy approved changes</b> and hand the result to your agent.
     <b>{{SUMMARY}}</b></p>
  <p class="std"><span class="stdtag">Standard: WCAG 2.1 Level AA</span>
     normal text needs <b>4.5:1</b>; large/bold text and non-text UI components (the dashed
     target, draggables, buttons, checkbox outlines) need <b>3:1</b>. Each badge shows the
     threshold it is judged against. (AAA would require 7:1 / 4.5:1 &mdash; not targeted here.)</p>
  <nav class="nav">{{NAV}}</nav>
</header>
<main>{{SECTIONS}}</main>
<div class="bar">
  <span class="count"><b id="n">0</b> fix(es) selected</span>
  <button class="copybtn" id="copy" disabled>Copy approved changes</button>
</div>
<div class="toast" id="toast"></div>
<div class="modal" id="modal"><div class="modal-box">
  <div class="modal-hd"><span>Approved changes</span><button id="mclose">Close</button></div>
  <p class="modal-tip">Copied to your clipboard if the browser allowed it. Otherwise: click in the
     box, Select All (Ctrl+A), Copy (Ctrl+C), and paste to your agent.</p>
  <textarea id="out" readonly></textarea>
</div></div>
<script>
  const opts = [...document.querySelectorAll('.fix-opt')];
  const toast = (msg) => { const t=document.getElementById('toast'); t.textContent=msg;
    t.classList.add('show'); setTimeout(()=>t.classList.remove('show'),1800); };
  const parseChanges = (o) => o.dataset.changes.split(';;').map(s=>{
    const i=s.indexOf('|'); return {var:s.slice(0,i), value:s.slice(i+1)}; });
  function refresh(){
    const checked = opts.filter(o=>o.checked);
    document.getElementById('n').textContent = checked.length;
    document.getElementById('copy').disabled = checked.length===0;
    document.querySelectorAll('.card.flagged').forEach(card=>{
      const any=[...card.querySelectorAll('.fix-opt')].some(o=>o.checked);
      card.classList.toggle('addressed', any);
    });
  }
  opts.forEach(cb=>cb.addEventListener('change', e=>{
    if(e.target.checked){ // one choice per element: untick siblings in the same group
      opts.filter(o=>o!==e.target && o.dataset.group===e.target.dataset.group)
          .forEach(o=>o.checked=false);
    }
    refresh();
  }));
  const modal=document.getElementById('modal'), outArea=document.getElementById('out');
  document.getElementById('mclose').onclick=()=>modal.classList.remove('show');
  modal.addEventListener('click', e=>{ if(e.target===modal) modal.classList.remove('show'); });
  function tryCopy(text){
    // 1) execCommand on a focused, selected textarea works inside sandboxed iframes where
    //    the async Clipboard API is blocked. 2) fall back to the async API. 3) the modal
    //    always shows the text so a manual Ctrl+C is possible regardless.
    outArea.value=text; modal.classList.add('show'); outArea.focus(); outArea.select();
    let ok=false; try{ ok=document.execCommand('copy'); }catch(e){}
    if(!ok && navigator.clipboard){ navigator.clipboard.writeText(text).then(
      ()=>toast('Copied to clipboard'), ()=>{}); }
    return ok;
  }
  document.getElementById('copy').addEventListener('click', ()=>{
    const checked = opts.filter(o=>o.checked);
    if(!checked.length) return;
    const byTheme={};
    checked.forEach(o=>{ (byTheme[o.dataset.theme] ??= []).push(o); });
    let out='# Approved game-theme contrast changes\n# Apply each line as a CSS variable in the named .bloom-page.game-theme-* block of gamesThemes.less\n';
    let lines=0;
    for(const [theme,list] of Object.entries(byTheme)){
      out += `\n## ${theme}\n`;
      const seen=new Set();  // a single change (e.g. one primary-color tweak) can fix several checks
      list.forEach(o=>{ parseChanges(o).forEach(c=>{
        const k=c.var+'='+c.value; if(seen.has(k)) return; seen.add(k);
        out += `${c.var}: ${c.value}; // ${o.dataset.note}\n`; lines++; }); });
    }
    const ok=tryCopy(out);
    toast(ok ? `Copied ${lines} line(s) for ${checked.length} fix(es)`
             : 'Select the text and press Ctrl+C');
  });
  refresh();
</script>
"""


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "game-theme-preview.html"
    less_path = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_LESS
    curated_path = sys.argv[3] if len(sys.argv) > 3 else None
    curated = {}
    if curated_path:
        import json
        with open(curated_path, encoding="utf-8") as f:
            curated = json.load(f)
    with open(less_path, encoding="utf-8") as f:
        themes = parse_themes(f.read())
    with open(out, "w", encoding="utf-8") as f:
        f.write(render(themes, curated))
    extra = f" (+ curated suggestions for {', '.join(curated)})" if curated else ""
    print(f"Wrote {out} covering {len(themes)} themes: {', '.join(themes)}{extra}")


if __name__ == "__main__":
    main()
