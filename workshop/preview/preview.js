// Browser stand-in for the C# view of the current layout. Type/sort, nominations and date defaults
// and all strings come from ConfigDefaults.cs / PluginConfig.cs / lang/de.json through
// window.PV_CONFIG (tools/sync_preview.py), so the mock follows the plugin instead of
// duplicating a second configuration.
// URL: ?n=8|45|all &state=vote|voted|reveal|empty|browse &dates=0 (hide dates) &clean=1 (no tool bar).
(() => {
  const q = new URLSearchParams(location.search);
  const BROWSE = document.body.dataset.browse === "1";
  const CONFIG = window.PV_CONFIG;
  const L = CONFIG.text;
  const SHOW_DATES = q.has("dates") ? q.get("dates") !== "0" : CONFIG.showAddedDates;
  const SLOTS = document.querySelectorAll('[id^="mv-slot-"]').length;
  const TYPE_SLOTS = document.querySelectorAll('[id^="mv-ft-"]').length;
  const SORT_SLOTS = document.querySelectorAll('[id^="mv-fs-"]').length;
  const N_OPTIONS = [8, 45, SLOTS];
  const N = N_OPTIONS.includes(Number(q.get("n"))) ? Number(q.get("n")) : 45;
  const STATE = BROWSE ? "browse" : q.get("state") || "vote";
  const RANKS = 6;

  const fmt = (key, ...values) => {
    let value = L[key] ?? key;
    values.forEach((part, index) => {
      value = value.split(`{${index}}`).join(String(part));
    });
    return value;
  };

  // Sample catalog: maps that have a preview image, then workshop stand-ins up to the XML pool.
  const LONG = ["ws_ancient_night_extended_edition", "ws_aim_redline_training_complex", "ws_surf_kitsune_reborn"];
  const base = window.PV_MAPS.slice();
  const maps = [];
  for (let i = 0; maps.length < SLOTS; i++) {
    if (i < base.length) maps.push({ id: base[i], img: base[i] });
    else maps.push({ id: LONG[i - base.length] || `ws_community_map_${i - base.length + 1}`, img: base[i % base.length] });
  }
  const all = maps.slice(0, N);
  const mapById = new Map(all.map((map) => [map.id, map]));
  const position = new Map(all.map((map, index) => [map.id, index]));
  const byId = (id) => mapById.get(id);
  const current = byId("de_dust2") || all[0];

  // MapNameDisplay.Format: drop a leading token of at most 4 chars, then title-case words.
  const pretty = (id) => {
    const separator = id.indexOf("_");
    const name = separator > 0 && separator <= 4 && separator + 1 < id.length
      ? id.slice(separator + 1)
      : id;
    if (!name.includes("_")) {
      return name ? name[0].toUpperCase() + name.slice(1) : name;
    }
    return name.split("_").filter(Boolean)
      .map((word) => word[0].toUpperCase() + word.slice(1)).join(" ");
  };

  const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const matchesPattern = (id, pattern) => {
    if (!pattern) return false;
    if (!pattern.includes("*")) {
      return id.toLowerCase().startsWith(pattern.toLowerCase());
    }
    const regex = new RegExp(`^${pattern.split("*").map(escapeRegExp).join(".*")}$`, "i");
    return regex.test(id);
  };
  const typeMatches = (map, index) => {
    const type = CONFIG.types[index];
    if (type.prefixes.length > 0) {
      return type.prefixes.some((prefix) => matchesPattern(map.id, prefix));
    }
    return !CONFIG.types.slice(0, index)
      .some((earlier) => earlier.prefixes.some((prefix) => matchesPattern(map.id, prefix)));
  };
  const TYPES = CONFIG.types.map((type, index) => ({
    ...type,
    test: (map) => typeMatches(map, index),
  }));
  const SORTS = CONFIG.sorts;
  const DEFAULT_SORT = SORTS.findIndex((sort) => sort.by === "name" && sort.order === "asc");

  const s = {
    votes: {}, firstVote: [], mine: null, fav: new Set(), rating: null,
    likes: 12, dislikes: 3, type: 0, favOnly: false, sort: DEFAULT_SORT,
    reveal: false, minimized: false, closed: false,
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
  if (STATE === "empty") {
    s.type = Math.max(0, TYPES.findIndex((type) => type.id === "hostage"));
    s.favOnly = true;
  }

  // ---- DOM helpers (mirror the three server channels) ----
  const $ = (id) => document.getElementById(id);
  const text = (name, value) => document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => (el.textContent = value));
  const flag = (id, cls, on) => $(id)?.classList.toggle(cls, !!on);
  const group = (id, classes, active) => { const el = $(id); if (el) classes.forEach((c) => el.classList.toggle(c, c === active)); };
  const shot = (id, map) => { const el = $(id); if (el) el.style.backgroundImage = map ? `url("maps/${map.img}.webp")` : ""; };
  const VIEWS = ["view-vote", "view-reveal"];
  const SHARES = Array.from({ length: 11 }, (_, i) => `p${i * 10}`);

  const ranked = () =>
    s.firstVote.map((id, i) => ({ id, c: s.votes[id], i })).sort((a, b) => b.c - a.c || a.i - b.i).map((x) => x.id);
  const plays = (map) => (map.id.length * 7) % 13;
  const rating = (map) => (map.id.length % 5) - 2;
  const addedAt = (map) => Date.UTC(2024, 0, 1) / 1000 + position.get(map.id) * 86400 * 7;
  const likes = (map) => 40 + ((map.id.length * 137 + position.get(map.id) * 311) % 2400);
  const dislikes = (map) => 2 + ((map.id.length * 53 + position.get(map.id) * 97) % 180);
  // Sample edge cases for the tile rating bar and date: every 9th map unrated, every 7th has no date.
  const unrated = (map) => position.get(map.id) % 9 === 8;
  const known = (map) => position.get(map.id) % 7 !== 6;
  // VoteView.RatingStep: -1 unrated, mixed ratings clamped to p10..p90.
  const ratingStep = (up, down) => {
    if (up + down <= 0) return -1;
    const step = Math.round((up * 10) / (up + down));
    return up > 0 && down > 0 ? Math.min(9, Math.max(1, step)) : step;
  };
  // VoteView.FormatAdded: UTC day in the lang format, empty when unknown.
  const formatAdded = (unix) => {
    if (!unix) return "";
    const d = new Date(unix * 1000);
    const pad = (n) => String(n).padStart(2, "0");
    return (L["hud.added_format"] || "yyyy-MM-dd")
      .replace("yyyy", d.getUTCFullYear()).replace("MM", pad(d.getUTCMonth() + 1)).replace("dd", pad(d.getUTCDate()));
  };
  // Mirrors VoteView.FormatCount with the viewer's configured separator.
  const GROUP_SEPARATOR = L["number.group_separator"] || ",";
  const formatCount = (value) => {
    if (value < 100000) return String(value).replace(/\B(?=(\d{3})+(?!\d))/g, GROUP_SEPARATOR);
    if (value < 1000000) return `${Math.floor(value / 1000)}k`;
    return `${Math.floor(value / 100000) / 10}`.replace(/\.0$/, "") + "M";
  };
  const sortValue = (map, by) => {
    if (by === "plays") return plays(map);
    if (by === "rating") return rating(map);
    if (by === "favorite") return s.fav.has(map.id) ? 1 : 0;
    if (by === "newest" || by === "oldest") return addedAt(map);
    return pretty(map.id);
  };
  const order = () => {
    const list = all.slice();
    if (s.sort < 0 || s.sort >= SORTS.length) return list;
    const sort = SORTS[s.sort];
    if (!["name", "plays", "rating", "favorite", "newest", "oldest"].includes(sort.by)) return list;
    const descending = sort.order !== "asc";
    list.sort((a, b) => {
      const av = sortValue(a, sort.by);
      const bv = sortValue(b, sort.by);
      let result = typeof av === "string"
        ? (av < bv ? -1 : av > bv ? 1 : 0)
        : av - bv;
      if (descending) result = -result;
      return result || position.get(a.id) - position.get(b.id);
    });
    return list;
  };
  const availableFavorites = () => all.filter((map) => s.fav.has(map.id)).length;
  const visible = (map) => TYPES[s.type].test(map) && (!s.favOnly || s.fav.has(map.id));

  let grid = [];
  const paint = () => {
    group("Vote", VIEWS, s.reveal ? "view-reveal" : "view-vote");
    flag("Vote", "is-off", s.closed);
    flag("Vote", "is-minimized", s.minimized);
    flag("Vote", "dates-off", !SHOW_DATES);
    flag("mv-timer", "is-off", BROWSE);
    text("mv_title", BROWSE ? fmt("hud.maps_title", all.length) : L["hud.title"]);
    text("mv_hide", BROWSE ? L["hud.close"] : s.minimized ? L["hud.show"] : L["hud.hide"]);

    const mine = byId(s.mine);
    flag("mv-status", "is-self", !!mine);
    let status;
    if (BROWSE) {
      status = mine
        ? fmt("hud.status_nominated", pretty(mine.id))
        : CONFIG.nominationsEnabled ? L["hud.status_nominate_hint"] : L["hud.status_browse"];
    } else if (s.reveal) {
      status = L["hud.status_closed"];
    } else {
      status = mine ? fmt("hud.status_voted", pretty(mine.id)) : L["hud.status_hint"];
    }
    text("mv_status", status);

    // Current map + rating
    text("mv_cur_tag", L["hud.current"]);
    text("mv_cur_name", pretty(current.id));
    shot("mv-current-shot", current);
    flag("mv-current-fav", "is-active", s.fav.has(current.id));
    text("mv_rate_q", L["hud.rate_q"]);
    text("mv_like", L["hud.like"]);
    text("mv_dislike", L["hud.dislike"]);
    text("mv_like_n", String(s.likes));
    text("mv_dislike_n", String(s.dislikes));
    flag("mv-like", "is-active", s.rating === "like");
    flag("mv-dislike", "is-active", s.rating === "dislike");

    // Ranking
    const top = ranked();
    const total = top.reduce((n, id) => n + s.votes[id], 0);
    text("mv_rank_title", L["hud.rank_title"]);
    text("mv_rank_empty", L["hud.rank_empty"]);
    text("mv_self", L["hud.self"]);
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
    for (let i = 0; i < TYPE_SLOTS; i++) {
      const type = TYPES[i];
      flag(`mv-ft-${i}`, "is-off", !type);
      if (!type) continue;
      const count = all.filter(type.test).length;
      text(`ft${i}`, `${type.label} (${count})`);
      flag(`mv-ft-${i}`, "is-active", s.type === i);
      flag(`mv-ft-${i}`, "is-disabled", i > 0 && count === 0);
    }

    const favoriteCount = availableFavorites();
    if (favoriteCount === 0) s.favOnly = false;
    text("mv_favonly", `${L["hud.fav_only"]} (${favoriteCount})`);
    flag("mv-fav-only", "is-active", s.favOnly);
    flag("mv-fav-only", "is-disabled", favoriteCount === 0);

    for (let i = 0; i < SORTS.length && i < SORT_SLOTS; i++) {
      const sort = SORTS[i];
      flag(`mv-fs-${i}`, "is-off", !sort);
      if (!sort) continue;
      text(`fs${i}`, sort.label);
      flag(`mv-fs-${i}`, "is-active", s.sort === i);
    }

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
      const isCurrent = m.id === current.id;
      const up = isCurrent ? s.likes : unrated(m) ? 0 : likes(m);
      const down = isCurrent ? s.dislikes : unrated(m) ? 0 : dislikes(m);
      text(`ln${i}`, formatCount(up));
      text(`dn${i}`, formatCount(down));
      const step = ratingStep(up, down);
      SHARES.forEach((c, k) => flag(`mv-${i}`, c, k === step));
      if (SHOW_DATES) text(`ad${i}`, formatAdded(known(m) ? addedAt(m) : 0));
      group(`mv-${i}`, ["votes-none", "votes-some"], (s.votes[m.id] || 0) > 0 ? "votes-some" : "votes-none");
      flag(`mv-slot-${i}`, "is-self", m.id === s.mine);
      flag(`mv-fav-${i}`, "is-active", s.fav.has(m.id));
    }
    flag("mv-empty", "is-off", shown > 0);
    text("mv_empty", L["hud.empty"]);

    // Reveal
    const winner = byId(top[0]) || all[0];
    text("mv_win_tag", L["hud.next"]);
    text("mv_win_name", pretty(winner.id));
    const winnerVotes = s.votes[winner.id] || 0;
    text("mv_win_votes", winnerVotes === 1
      ? L["hud.win_votes_one"]
      : winnerVotes ? fmt("hud.win_votes", winnerVotes) : L["hud.win_random"]);
    shot("mv-win-shot", winner);
  };

  // ---- Client-timed countdown: span-N, then is-running in the next frame; text 1x/s ----
  let left = BROWSE ? 0 : STATE === "reveal" ? 5 : 12;
  const fmtTime = (t) => `0:${String(Math.max(0, t)).padStart(2, "0")}`;
  const tick = () => {
    text("mv_time", fmtTime(left));
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
    if (id === "mv-minimize") {
      if (BROWSE) s.closed = true;
      else s.minimized = !s.minimized;
    } else if (s.reveal) {
      // Reveal only exposes the hide/show control, matching the server.
      return;
    } else if (id === "mv-current-fav") {
      s.fav.has(current.id) ? s.fav.delete(current.id) : s.fav.add(current.id);
    } else if (id === "mv-like" || id === "mv-dislike") {
      const next = id === "mv-like" ? "like" : "dislike";
      if (s.rating === "like") s.likes--;
      if (s.rating === "dislike") s.dislikes--;
      s.rating = s.rating === next ? null : next;
      if (s.rating === "like") s.likes++;
      if (s.rating === "dislike") s.dislikes++;
    } else if ((m = /^mv-ft-(\d)$/.exec(id))) {
      const index = +m[1];
      if (index < TYPES.length && (index === 0 || all.filter(TYPES[index].test).length > 0)) {
        s.type = s.type === index ? 0 : index;
      }
    } else if (id === "mv-fav-only" && availableFavorites() > 0) {
      s.favOnly = !s.favOnly;
    } else if ((m = /^mv-fs-(\d)$/.exec(id))) {
      s.sort = s.sort === +m[1] ? -1 : +m[1];
    } else if ((m = /^mv-fav-(\d+)$/.exec(id))) {
      const map = grid[+m[1]];
      if (map) s.fav.has(map.id) ? s.fav.delete(map.id) : s.fav.add(map.id);
    } else if ((m = /^mv-(\d+)$/.exec(id)) || (m = /^mv-rank-(\d)$/.exec(id))) {
      if (BROWSE && !CONFIG.nominationsEnabled) return;
      const map = id.startsWith("mv-rank-") ? byId(ranked()[+m[1]]) : grid[+m[1]];
      if (!map || map.id === s.mine) return;
      if (s.mine) vote(s.mine, -1);
      vote(map.id, 1);
      s.mine = map.id;
    }
    paint();
  });

  // ---- Preview tool bar ----
  if (q.get("clean") !== "1") {
    const bar = document.createElement("div");
    bar.className = "pv-bar";
    const pages = window.PV_PAGES || { vote: "vote_v3.html", maps: "maps_v3.html" };
    const link = (label, params, active) =>
      `<a class="${active ? "is-active" : ""}" href="${params.browse ? pages.maps : pages.vote}?n=${params.n}&state=${params.state}">${label}</a>`;
    bar.innerHTML =
      N_OPTIONS.map((n) => link(`${n} maps`, { n, state: STATE, browse: BROWSE }, n === N)).join("") +
      '<span class="pv-sep"></span>' +
      ["vote", "voted", "reveal", "empty"].map((st) => link(st, { n: N, state: st, browse: BROWSE }, st === STATE)).join("") +
      link("browse", { n: N, state: "browse", browse: true }, BROWSE);
    document.body.appendChild(bar);
  }
  paint();
})();
