// Browser stand-in for the C# view of layout v2 (T8). Writes the same ids / classes / dialog
// variables as the contract in tools/generate_vote_xml_v2.py, so the preview shows what the
// server would paint. URL: ?n=8|45|100 &state=vote|voted|reveal|empty|browse &clean=1 (no tool bar).
(() => {
  const q = new URLSearchParams(location.search);
  const BROWSE = document.body.dataset.browse === "1";
  const N = [8, 45, 100].includes(Number(q.get("n"))) ? Number(q.get("n")) : 45;
  const STATE = BROWSE ? "browse" : q.get("state") || "vote";
  const SLOTS = 100;
  const RANKS = 6;

  // Sample catalog: maps that have a preview image, then workshop stand-ins up to 100 (images reused).
  const LONG = ["ws_ancient_night_extended_edition", "ws_aim_redline_training_complex", "ws_surf_kitsune_reborn"];
  const base = window.PV_MAPS.slice();
  const maps = [];
  for (let i = 0; maps.length < 100; i++) {
    if (i < base.length) maps.push({ id: base[i], img: base[i] });
    else maps.push({ id: LONG[i - base.length] || `ws_community_map_${i - base.length + 1}`, img: base[i % base.length] });
  }
  const pretty = (id) =>
    id.replace(/^(de|cs|ar|ws)_/, "").split("_").map((w) => w[0].toUpperCase() + w.slice(1)).join(" ");
  const all = maps.slice(0, N);
  const current = all.find((m) => m.id === "de_dust2") || all[0];

  const TYPES = [
    { label: "Alle", test: () => true },
    { label: "Bomb", test: (m) => m.id.startsWith("de_") },
    { label: "Hostage", test: (m) => m.id.startsWith("cs_") },
    { label: "Arms Race", test: (m) => m.id.startsWith("ar_") },
    { label: "Andere", test: (m) => !/^(de|cs|ar)_/.test(m.id) },
  ];
  const SORTS = ["Meistgespielt", "Wenig gespielt", "Beliebt", "Unbeliebt"];

  const s = {
    votes: {}, firstVote: [], mine: null, fav: new Set(), rating: null,
    likes: 12, dislikes: 3, type: 0, favOnly: false, sort: -1,
    reveal: false, minimized: false,
  };
  const vote = (id, delta) => {
    s.votes[id] = Math.max(0, (s.votes[id] || 0) + delta);
    if (s.votes[id] > 0 && !s.firstVote.includes(id)) s.firstVote.push(id);
    if (!s.votes[id]) s.firstVote = s.firstVote.filter((x) => x !== id);
  };
  const seed = () => {
    [["de_mirage", 4], ["de_inferno", 2], ["de_nuke", 1], ["cs_office", 1], ["de_ancient", 1]].forEach(([id, c]) => {
      if (all.some((m) => m.id === id)) vote(id, c);
    });
    s.mine = !BROWSE && all.some((m) => m.id === "de_mirage") ? "de_mirage" : null;
    s.fav = new Set(["de_mirage", "de_nuke", "de_vertigo"].filter((id) => all.some((m) => m.id === id)));
  };
  if (STATE !== "vote") seed();
  if (STATE === "voted" || STATE === "reveal") { s.rating = "like"; s.likes += 1; }
  if (STATE === "reveal") s.reveal = true;
  if (STATE === "empty") { s.type = 2; s.favOnly = true; }
  if (STATE === "browse") s.sort = 0;

  // ---- DOM helpers (mirror the three server channels) ----
  const $ = (id) => document.getElementById(id);
  const text = (name, value) => document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => (el.textContent = value));
  const flag = (id, cls, on) => $(id)?.classList.toggle(cls, !!on);
  const group = (id, classes, active) => { const el = $(id); if (el) classes.forEach((c) => el.classList.toggle(c, c === active)); };
  const shot = (id, map) => { const el = $(id); if (el) el.style.backgroundImage = map ? `url("maps/${map.img}.webp")` : ""; };
  const VIEWS = ["view-vote", "view-reveal", "view-browse"];
  const SHARES = Array.from({ length: 11 }, (_, i) => `p${i * 10}`);

  const ranked = () =>
    s.firstVote.map((id, i) => ({ id, c: s.votes[id], i })).sort((a, b) => b.c - a.c || a.i - b.i).map((x) => x.id);
  const order = () => {
    const plays = (m) => (m.id.length * 7) % 13;
    const list = all.slice();
    if (s.sort === 0) list.sort((a, b) => plays(b) - plays(a));
    if (s.sort === 1) list.sort((a, b) => plays(a) - plays(b));
    if (s.sort === 2) list.sort((a, b) => (b.id.length % 5) - (a.id.length % 5));
    if (s.sort === 3) list.sort((a, b) => (a.id.length % 5) - (b.id.length % 5));
    return list;
  };
  const visible = (m) => TYPES[s.type].test(m) && (!s.favOnly || s.fav.has(m.id));
  const byId = (id) => all.find((m) => m.id === id);

  let grid = [];
  const paint = () => {
    group("Vote", VIEWS, BROWSE ? "view-browse" : s.reveal ? "view-reveal" : "view-vote");
    flag("Vote", "is-minimized", s.minimized);
    text("mv_title", BROWSE ? "Maps auf diesem Server" : "Stimme für die nächste Map");
    text("mv_hide", BROWSE ? "Schließen" : s.minimized ? "Einblenden" : "Ausblenden");

    const mine = byId(s.mine);
    flag("mv-status", "is-self", !!mine && !BROWSE);
    text("mv_status", BROWSE
      ? "Stern = Favorit · Bewerten geht hier jederzeit"
      : s.reveal ? "Abstimmung beendet"
      : mine ? `Deine Stimme: ${pretty(mine.id)} – klick eine andere Map, um zu wechseln`
      : "Klick auf eine Map, um abzustimmen");

    // Current map + rating
    text("mv_cur_tag", "Aktuelle Map");
    text("mv_cur_name", pretty(current.id));
    shot("mv-current-shot", current);
    flag("mv-current-fav", "is-active", s.fav.has(current.id));
    text("mv_rate_q", "Wie war die Map?");
    text("mv_like", "Gefällt mir");
    text("mv_dislike", "Gefällt mir nicht");
    text("mv_like_n", String(s.likes));
    text("mv_dislike_n", String(s.dislikes));
    flag("mv-like", "is-active", s.rating === "like");
    flag("mv-dislike", "is-active", s.rating === "dislike");

    // Ranking
    const top = ranked();
    const total = top.reduce((n, id) => n + s.votes[id], 0);
    text("mv_rank_title", "Vorne");
    text("mv_rank_empty", "Noch keine Stimmen – wähl unten eine Map");
    text("mv_self", "Deine Stimme");
    flag("mv-rank-empty", "is-off", top.length > 0);
    for (let i = 0; i < RANKS; i++) {
      const m = byId(top[i]);
      flag(`mv-rank-${i}`, "is-off", !m);
      if (!m) continue;
      shot(`mv-rank-shot-${i}`, m);
      text(`rn${i}`, pretty(m.id));
      text(`rv${i}`, String(s.votes[m.id]));
      group(`mv-rank-fill-${i}`, SHARES, `p${Math.round((s.votes[m.id] / total) * 10) * 10}`);
      flag(`mv-rank-self-${i}`, "is-self", m.id === s.mine);
    }

    // Filters (labels carry the count)
    for (let i = 0; i < 8; i++) {
      const t = TYPES[i];
      flag(`mv-ft-${i}`, "is-off", !t);
      if (!t) continue;
      const count = all.filter(t.test).length;
      text(`ft${i}`, `${t.label} ${count}`);
      flag(`mv-ft-${i}`, "is-active", s.type === i);
      flag(`mv-ft-${i}`, "is-disabled", i > 0 && count === 0);
    }
    text("mv_favonly", `Favoriten ${s.fav.size}`);
    flag("mv-fav-only", "is-active", s.favOnly);
    flag("mv-fav-only", "is-disabled", s.fav.size === 0);
    SORTS.forEach((label, i) => {
      flag(`mv-fs-${i}`, "is-off", false);
      text(`fs${i}`, label);
      flag(`mv-fs-${i}`, "is-active", s.sort === i);
    });

    // Grid: order is global, visibility per player
    grid = order();
    let shown = 0;
    for (let i = 0; i < SLOTS; i++) {
      const m = grid[i];
      const on = !!m && visible(m);
      flag(`mv-slot-${i}`, "is-off", !on);
      if (!m) continue;
      shown += on ? 1 : 0;
      shot(`mv-shot-${i}`, m);
      text(`n${i}`, pretty(m.id));
      text(`v${i}`, String(s.votes[m.id] || 0));
      group(`mv-${i}`, ["votes-none", "votes-some"], (s.votes[m.id] || 0) > 0 ? "votes-some" : "votes-none");
      flag(`mv-slot-${i}`, "is-self", m.id === s.mine);
      flag(`mv-fav-${i}`, "is-active", s.fav.has(m.id));
    }
    flag("mv-empty", "is-off", shown > 0);
    text("mv_empty", "Keine Map passt zu diesem Filter – Filter abwählen, um alle zu sehen");

    // Reveal
    const winner = byId(top[0]) || all[0];
    text("mv_win_tag", "Nächste Map");
    text("mv_win_name", pretty(winner.id));
    const wv = s.votes[winner.id] || 0;
    text("mv_win_votes", wv === 1 ? "1 Stimme" : wv ? `${wv} Stimmen` : "Zufallsauswahl – keine Stimmen");
    shot("mv-win-shot", winner);
  };

  // ---- Client-timed countdown: span-N, then is-running in the next frame; text 1x/s ----
  let left = BROWSE ? 0 : STATE === "reveal" ? 5 : 12;
  const fmt = (t) => `0:${String(Math.max(0, t)).padStart(2, "0")}`;
  const tick = () => {
    text("mv_time", fmt(left));
    flag("mv-timer", "is-urgent", left <= 5);
    if (left-- > 0) setTimeout(tick, 1000);
  };
  if (left > 0) {
    const fill = STATE === "reveal" ? "mv-timer-fill-reveal" : "mv-timer-fill";
    flag(fill, `span-${left}`, true);
    requestAnimationFrame(() => requestAnimationFrame(() => flag(fill, "is-running", true)));
    tick();
  }

  // ---- Clicks (the server would route by button id) ----
  $("Vote").addEventListener("click", (ev) => {
    const id = ev.target.closest("button[id]")?.id;
    if (!id) return;
    let m;
    if (id === "mv-minimize") s.minimized = !s.minimized;
    else if (id === "mv-current-fav") s.fav.has(current.id) ? s.fav.delete(current.id) : s.fav.add(current.id);
    else if ((id === "mv-like" || id === "mv-dislike") && !s.reveal) {
      const r = id === "mv-like" ? "like" : "dislike";
      if (s.rating === "like") s.likes--;
      if (s.rating === "dislike") s.dislikes--;
      s.rating = s.rating === r ? null : r;
      if (s.rating === "like") s.likes++;
      if (s.rating === "dislike") s.dislikes++;
    } else if ((m = /^mv-ft-(\d)$/.exec(id))) s.type = s.type === +m[1] ? 0 : +m[1];
    else if (id === "mv-fav-only" && s.fav.size) s.favOnly = !s.favOnly;
    else if ((m = /^mv-fs-(\d)$/.exec(id))) s.sort = s.sort === +m[1] ? -1 : +m[1];
    else if ((m = /^mv-fav-(\d+)$/.exec(id))) { const k = grid[+m[1]].id; s.fav.has(k) ? s.fav.delete(k) : s.fav.add(k); }
    else if (!BROWSE && !s.reveal && ((m = /^mv-(\d+)$/.exec(id)) || (m = /^mv-rank-(\d)$/.exec(id)))) {
      const k = id.startsWith("mv-rank-") ? ranked()[+m[1]] : grid[+m[1]].id;
      if (k !== s.mine) { if (s.mine) vote(s.mine, -1); vote(k, 1); s.mine = k; }
    }
    paint();
  });

  // ---- Preview tool bar ----
  if (q.get("clean") !== "1") {
    const bar = document.createElement("div");
    bar.className = "pv-bar";
    const link = (label, params, active) =>
      `<a class="${active ? "is-active" : ""}" href="${params.browse ? "maps_v2.html" : "vote_v2.html"}?n=${params.n}&state=${params.state}">${label}</a>`;
    bar.innerHTML =
      [8, 45, 100].map((n) => link(`${n} maps`, { n, state: STATE, browse: BROWSE }, n === N)).join("") +
      '<span class="pv-sep"></span>' +
      ["vote", "voted", "reveal", "empty"].map((st) => link(st, { n: N, state: st }, st === STATE)).join("") +
      link("browse", { n: N, state: "browse", browse: true }, BROWSE);
    document.body.appendChild(bar);
  }
  paint();
})();
