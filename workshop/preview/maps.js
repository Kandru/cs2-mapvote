(() => {
  const BROWSE = true;

  const SLOT = (() => {
    let n = 0;
    document.querySelectorAll('[id^="mv-"]').forEach((el) => {
      const m = /^mv-(\d+)$/.exec(el.id);
      if (m) n = Math.max(n, Number(m[1]) + 1);
    });
    return n || 50;
  })();

  // Prefer maps that have screenshots under preview/maps/*.webp
  const MAPS = ["de_dust2","de_mirage","de_inferno","de_nuke","de_overpass","de_ancient","de_anubis","de_vertigo","cs_office","cs_italy","ar_baggage","ar_shoots","de_cache","de_train","cs_agency","cs_assault","cs_militia","de_basalt","de_brewery","de_edin","de_palacio","de_whistle","de_dogtown","de_jura","de_golden","de_palais","de_thera","de_mills","de_assembly","de_boulder","de_canals","de_lake","de_safehouse","de_shortdust","de_tuscan","de_cbble","cs_alpine","cs_shelter","ar_pool_day","de_debris","de_eldorado","de_fachwerk","de_grail","de_memento","de_poseidon","de_rooftop","de_sanctum","de_stronghold","de_sugarcane","de_transit","ar_shoots_night","de_ancient_night","de_cbble_csgo","de_warden","rush_001"].slice(0, SLOT);

  const stem = (map) => map.replace(/[^a-z0-9_]/gi, "_").toLowerCase();

  const applyShot = (el, map) => {
    const s = stem(map);
    el.classList.add("MapShot");
    el.style.backgroundImage = `url("maps/${s}.webp")`;
    el.style.backgroundSize = "350px 197px";
    el.style.backgroundPosition = "50% 50%";
    el.style.backgroundRepeat = "no-repeat";
    const probe = new Image();
    // Clear overlay on miss so the default MapShot sibling shows through.
    probe.onerror = () => {
      el.style.backgroundImage = "";
    };
    probe.src = `maps/${s}.webp`;
  };

  const TYPES = [
    { id: "all", label: "All", prefixes: null },
    { id: "bomb", label: "Bomb", prefixes: ["de_"] },
    { id: "hostage", label: "Hostage", prefixes: ["cs_"] },
    { id: "arms_race", label: "Arms Race", prefixes: ["ar_"] },
    { id: "danger_zone", label: "Danger Zone", prefixes: ["dz_"] },
    { id: "other", label: "Other", prefixes: [] },
  ];
  const SORTS = [
    { id: "most_played", label: "Most played" },
    { id: "least_played", label: "Least played" },
    { id: "most_liked", label: "Most liked" },
    { id: "least_liked", label: "Least liked" },
    { id: "favorites", label: "Favorites" },
  ];

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    plays: Object.fromEntries(MAPS.map((m, i) => [m, (MAPS.length - i) % 17])),
    // Server-wide aggregate counters plus this browser's personal rating.
    likes: Object.fromEntries(MAPS.map((m, i) => [m, (MAPS.length - i + 3) % 9])),
    dislikes: Object.fromEntries(MAPS.map((m, i) => [m, (i + 1) % 5])),
    ratings: {},
    favorites: new Set(),
    current: MAPS[13] || MAPS[0],
    firstVote: [],
    mine: null,
    mineSet: new Set(),
    mode: "single", // single = CS2 one-vote; multi = toggle many maps
    reveal: false,
    winner: null,
    type: 0,
    sort: -1,
  };

  const isMine = (map) =>
    state.mode === "multi" ? state.mineSet.has(map) : state.mine === map;

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const ratingScore = (map) => (state.likes[map] || 0) - (state.dislikes[map] || 0);
  // 100+ saturates so the 28px catalog badge never clips.
  const ratingText = (score) =>
    score >= 100 ? "99+" : score <= -100 ? "-99" : score > 0 ? `+${score}` : String(score);

  const clearTile = (slotId, tileId, shotId, favId, voteVar, nameVar, ratingId, ratingVar) => {
    const slot = document.getElementById(slotId);
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    const fav = document.getElementById(favId);
    if (!slot || !tile || !shot) return;
    slot.classList.add("is-off");
    tile.classList.remove("is-self", "is-done", "is-lost", "is-voted", "is-fav");
    fav?.classList.remove("is-on");
    document.getElementById(ratingId)?.classList.add("is-off");
    shot.className = "MapShot";
    shot.style.backgroundImage = "";
    setVar(voteVar, "");
    setVar(nameVar, "");
    setVar(ratingVar, "");
  };

  const paintTile = (slotId, tileId, shotId, favId, voteVar, nameVar, ratingId, ratingVar, map) => {
    const slot = document.getElementById(slotId);
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    const fav = document.getElementById(favId);
    if (!slot || !tile || !shot) return;
    const count = state.votes[map] || 0;
    const score = ratingScore(map);
    const rated = (state.likes[map] || 0) + (state.dislikes[map] || 0) > 0;
    const isWinner = state.winner === map;
    const isLost = state.reveal && state.winner && !isWinner;
    slot.classList.remove("is-off");
    tile.classList.toggle("is-voted", count > 0);
    tile.classList.toggle("is-fav", state.favorites.has(map));
    tile.classList.toggle("is-self", !state.reveal && isMine(map));
    tile.classList.toggle("is-done", isWinner);
    tile.classList.toggle("is-lost", !!isLost);
    fav?.classList.toggle("is-on", state.favorites.has(map));
    const rating = document.getElementById(ratingId);
    if (rating) {
      rating.classList.toggle("is-off", !rated);
      rating.classList.toggle("is-positive", rated && score > 0);
      rating.classList.toggle("is-negative", rated && score < 0);
    }
    applyShot(shot, map);
    setVar(voteVar, String(count));
    setVar(nameVar, map);
    setVar(ratingVar, rated ? ratingText(score) : "");
  };

  const matchesType = (map, type) => {
    if (!type || type.prefixes === null) return true;
    if (type.prefixes.length === 0) {
      return !TYPES.some(
        (t) => t.prefixes && t.prefixes.length > 0 && t.prefixes.some((p) => map.startsWith(p))
      );
    }
    return type.prefixes.some((p) => map.startsWith(p));
  };

  const filteredMaps = () => {
    const type = TYPES[state.type] || TYPES[0];
    let maps = state.maps.filter((m) => matchesType(m, type));
    const sort = SORTS[state.sort];
    if (sort?.id === "most_played") {
      maps = maps
        .slice()
        .sort((a, b) => (state.plays[b] || 0) - (state.plays[a] || 0) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (sort?.id === "least_played") {
      maps = maps
        .slice()
        .sort((a, b) => (state.plays[a] || 0) - (state.plays[b] || 0) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (sort?.id === "most_liked") {
      maps = maps
        .slice()
        .sort((a, b) => ratingScore(b) - ratingScore(a) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (sort?.id === "least_liked") {
      maps = maps
        .slice()
        .sort((a, b) => ratingScore(a) - ratingScore(b) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (sort?.id === "favorites") {
      maps = maps.filter((m) => state.favorites.has(m));
    }
    return maps;
  };

  const typeHasMaps = (type) =>
    state.maps.some((m) => matchesType(m, type));

  const sortEnabled = (sort) =>
    sort.id !== "favorites" || state.favorites.size > 0;

  const paintFilters = () => {
    const bar = document.getElementById("mv-filters");
    if (bar) bar.classList.remove("is-off");

    // Drop a type selection that no longer has maps (mirrors C# PaintFilterBar).
    if (state.type > 0 && state.type < TYPES.length && !typeHasMaps(TYPES[state.type])) {
      state.type = 0;
    }
    if (state.sort >= 0 && state.sort < SORTS.length && !sortEnabled(SORTS[state.sort])) {
      state.sort = -1;
    }

    for (let i = 0; i < 8; i++) {
      const chip = document.getElementById(`mv-ft-${i}`);
      if (!chip) continue;
      if (i < TYPES.length) {
        const disabled = i > 0 && !typeHasMaps(TYPES[i]);
        chip.classList.remove("is-off");
        chip.classList.toggle("is-disabled", disabled);
        chip.classList.toggle("is-active", state.type === i);
        setVar(`ft${i}`, TYPES[i].label);
      } else {
        chip.classList.add("is-off");
        chip.classList.remove("is-disabled", "is-active");
      }
    }

    const favSortIndex = SORTS.findIndex((s) => s.id === "favorites");
    const favChip = document.getElementById("mv-sort-fav");
    if (favChip) {
      if (favSortIndex >= 0) {
        const disabled = !sortEnabled(SORTS[favSortIndex]);
        favChip.classList.remove("is-off");
        favChip.classList.toggle("is-disabled", disabled);
        favChip.classList.toggle("is-active", state.sort === favSortIndex);
        setVar("sfav", SORTS[favSortIndex].label);
      } else {
        favChip.classList.add("is-off");
        favChip.classList.remove("is-disabled", "is-active");
      }
    }

    // mv-fs-* chips paint non-favorites sorts only (favorites lives next to All).
    const otherSorts = SORTS.map((s, i) => ({ s, i })).filter(({ s }) => s.id !== "favorites");
    for (let i = 0; i < 4; i++) {
      const chip = document.getElementById(`mv-fs-${i}`);
      if (!chip) continue;
      if (i < otherSorts.length) {
        const { s, i: sortIndex } = otherSorts[i];
        const disabled = !sortEnabled(s);
        chip.classList.remove("is-off");
        chip.classList.toggle("is-disabled", disabled);
        chip.classList.toggle("is-active", state.sort === sortIndex);
        setVar(`fs${i}`, s.label);
      } else {
        chip.classList.add("is-off");
        chip.classList.remove("is-disabled", "is-active");
      }
    }
  };

  const setLadder = (el, prefix, max, value) => {
    if (!el) return;
    for (let n = 0; n <= max; n++) el.classList.toggle(`${prefix}-${n}`, n === value);
  };

  const votedOrder = () => {
    const alive = state.firstVote.filter((m) => (state.votes[m] || 0) > 0);
    return alive
      .map((m, i) => ({ m, c: state.votes[m] || 0, i }))
      .sort((a, b) => b.c - a.c || a.i - b.i)
      .map((x) => x.m);
  };

  const paintCurrent = () => {
    const map = state.current;
    document.getElementById("mv-current")?.classList.toggle("is-off", !map);
    setVar("mv_current", "Current map");
    setVar("mv_cur_name", map || "");
    const score = map ? ratingScore(map) : 0;
    setVar("mv_cur_rating", ratingText(score));
    const rating = document.getElementById("mv-current-rating");
    if (rating) {
      rating.classList.toggle("is-positive", score > 0);
      rating.classList.toggle("is-negative", score < 0);
    }
    const shot = document.getElementById("mv-current-shot");
    if (shot) {
      if (map) applyShot(shot, map);
      else {
        shot.className = "MapShot mv-map-default";
        shot.style.backgroundImage = "";
      }
    }
    const isFav = !!map && state.favorites.has(map);
    document.getElementById("mv-current-fav")?.classList.toggle("is-on", isFav);
    document.getElementById("mv-current-image")?.classList.toggle("is-fav", isFav);
    document.getElementById("mv-current-like")?.classList.toggle("is-on", !!map && state.ratings[map] === "like");
    document.getElementById("mv-current-dislike")?.classList.toggle("is-on", !!map && state.ratings[map] === "dislike");
  };

  const paintGrid = (grid) => {
    for (let i = 0; i < SLOT; i++) {
      if (i < grid.length) {
        paintTile(
          `mv-slot-${i}`,
          `mv-${i}`,
          `mv-shot-${i}`,
          `mv-fav-${i}`,
          `v${i}`,
          `n${i}`,
          `mv-rate-${i}`,
          `r${i}`,
          grid[i]
        );
      } else {
        clearTile(
          `mv-slot-${i}`,
          `mv-${i}`,
          `mv-shot-${i}`,
          `mv-fav-${i}`,
          `v${i}`,
          `n${i}`,
          `mv-rate-${i}`,
          `r${i}`
        );
      }
    }
  };

  const paintVoted = (voted) => {
    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(
          `mv-voted-slot-${i}`,
          `mv-voted-${i}`,
          `mv-voted-shot-${i}`,
          `mv-voted-fav-${i}`,
          `vv${i}`,
          `vn${i}`,
          `mv-voted-rate-${i}`,
          `vr${i}`,
          voted[i]
        );
      } else {
        clearTile(
          `mv-voted-slot-${i}`,
          `mv-voted-${i}`,
          `mv-voted-shot-${i}`,
          `mv-voted-fav-${i}`,
          `vv${i}`,
          `vn${i}`,
          `mv-voted-rate-${i}`,
          `vr${i}`
        );
      }
    }
  };

  const paint = () => {
    const grid = filteredMaps();
    const empty = document.getElementById("mv-empty");
    if (empty) empty.classList.toggle("is-off", grid.length > 0);
    paintFilters();

    if (BROWSE) {
      setVar("mv_title", "Maps");
      setVar("mv_chip", "X");
      setVar("mv_empty", "No maps match this filter");
      paintGrid(grid);
      return;
    }

    const voted = votedOrder();
    const section = document.getElementById("mv-voted-section");
    const hint = document.getElementById("mv-voted-hint");
    if (section) section.classList.toggle("has-votes", voted.length > 0);
    if (hint) hint.classList.toggle("is-off", voted.length > 0 || state.reveal);
    setVar("mv_title", state.reveal ? "Winning map" : "Vote for the next map");
    setVar("mv_hint", "Cast the first vote");
    setVar("mv_chip", "Map Vote");
    setVar("mv_empty", "No maps match this filter");
    setLadder(document.getElementById("mv-voted-strip"), "vw", SLOT, voted.length);
    paintCurrent();
    paintVoted(voted);
    paintGrid(grid);
  };

  const bump = (map, delta) => {
    const next = Math.max(0, (state.votes[map] || 0) + delta);
    state.votes[map] = next;
    if (delta > 0 && next === delta && !state.firstVote.includes(map)) {
      state.firstVote.push(map);
    }
    if (next === 0) {
      state.firstVote = state.firstVote.filter((m) => m !== map);
    }
  };

  const castSingle = (map) => {
    const prev = state.mine;
    if (prev === map) return;
    if (prev) bump(prev, -1);
    state.mine = map;
    state.mineSet = new Set([map]);
    bump(map, 1);
  };

  const castMulti = (map, fromVoted) => {
    bump(map, fromVoted ? -1 : 1);
    if ((state.votes[map] || 0) > 0) state.mineSet.add(map);
    else state.mineSet.delete(map);
    state.mine = state.mineSet.size ? [...state.mineSet][0] : null;
  };

  const cast = (map, fromVoted) => {
    if (state.reveal || !map) return;
    if (state.mode === "multi") castMulti(map, fromVoted);
    else castSingle(map);
    paint();
  };

  const parseButton = (id) => {
    if (!id) return null;
    if (id === "mv-current-fav") return { currentFav: true };
    if (id === "mv-current-like") return { rate: "like" };
    if (id === "mv-current-dislike") return { rate: "dislike" };
    if (id.startsWith("mv-voted-fav-")) {
      const i = Number(id.slice("mv-voted-fav-".length));
      const order = votedOrder();
      if (!(Number.isInteger(i) && i >= 0 && i < order.length)) return null;
      return { fav: order[i] };
    }
    if (id.startsWith("mv-fav-")) {
      const i = Number(id.slice("mv-fav-".length));
      const grid = filteredMaps();
      if (!(Number.isInteger(i) && i >= 0 && i < grid.length)) return null;
      return { fav: grid[i] };
    }
    if (id.startsWith("mv-ft-")) {
      const i = Number(id.slice(6));
      if (Number.isInteger(i) && i >= 0 && i < TYPES.length) return { type: i };
      return null;
    }
    if (id === "mv-sort-fav") {
      const i = SORTS.findIndex((s) => s.id === "favorites");
      return i >= 0 ? { sort: i } : null;
    }
    if (id.startsWith("mv-fs-")) {
      const i = Number(id.slice(6));
      const otherSorts = SORTS.map((s, idx) => ({ s, idx })).filter(({ s }) => s.id !== "favorites");
      if (Number.isInteger(i) && i >= 0 && i < otherSorts.length) return { sort: otherSorts[i].idx };
      return null;
    }
    if (id.startsWith("mv-voted-")) {
      const rest = id.slice("mv-voted-".length);
      if (!/^\d+$/.test(rest)) return null;
      const i = Number(rest);
      const order = votedOrder();
      if (!(Number.isInteger(i) && i >= 0 && i < order.length)) return null;
      return { map: order[i], fromVoted: true };
    }
    if (/^mv-\d+$/.test(id)) {
      const i = Number(id.slice(3));
      const grid = filteredMaps();
      if (!(Number.isInteger(i) && i >= 0 && i < grid.length)) return null;
      return { map: grid[i], fromVoted: false };
    }
    return null;
  };

  const toggleMinimized = () => {
    document.getElementById("Vote")?.classList.toggle("is-minimized");
  };

  const toggleFavorite = (map) => {
    if (!map) return;
    if (state.favorites.has(map)) state.favorites.delete(map);
    else state.favorites.add(map);
    paint();
  };

  const toggleRating = (map, rating) => {
    if (!map) return;
    const previous = state.ratings[map];
    if (previous === "like") state.likes[map] = Math.max(0, (state.likes[map] || 0) - 1);
    if (previous === "dislike") state.dislikes[map] = Math.max(0, (state.dislikes[map] || 0) - 1);
    const next = previous === rating ? null : rating;
    if (next === "like") state.likes[map] = (state.likes[map] || 0) + 1;
    if (next === "dislike") state.dislikes[map] = (state.dislikes[map] || 0) + 1;
    if (next) state.ratings[map] = next;
    else delete state.ratings[map];
    paint();
  };

  document.getElementById("Vote")?.addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[id]");
    if (!btn) return;
    if (btn.id === "mv-minimize") {
      if (BROWSE) setBrowseOpen(false);
      else toggleMinimized();
      return;
    }
    const hit = parseButton(btn.id);
    if (!hit) return;
    if (hit.currentFav) {
      toggleFavorite(state.current);
      return;
    }
    if (hit.rate) {
      toggleRating(state.current, hit.rate);
      return;
    }
    if (hit.fav) {
      toggleFavorite(hit.fav);
      return;
    }
    if (hit.type !== undefined) {
      if (hit.type > 0 && !typeHasMaps(TYPES[hit.type])) return;
      state.type = hit.type;
      paint();
      return;
    }
    if (hit.sort !== undefined) {
      if (!sortEnabled(SORTS[hit.sort])) return;
      state.sort = state.sort === hit.sort ? -1 : hit.sort;
      paint();
      return;
    }
    if (hit.map && !BROWSE) cast(hit.map, hit.fromVoted);
  });

  const clearVotes = () => {
    state.votes = Object.fromEntries(state.maps.map((m) => [m, 0]));
    state.firstVote = [];
    state.mine = null;
    state.mineSet = new Set();
    state.reveal = false;
    state.winner = null;
    paint();
  };

  const seedVotes = () => {
    clearVotes();
    ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "cs_office"].forEach((m, idx) => {
      if (!state.maps.includes(m)) return;
      state.votes[m] = idx === 0 ? 3 : idx === 1 ? 2 : 1;
      state.firstVote.push(m);
    });
    state.mine = "de_dust2";
    state.mineSet = new Set(["de_dust2"]);
    paint();
  };

  const randomize = () => {
    clearVotes();
    const picks = state.maps.slice().sort(() => Math.random() - 0.5);
    const n = 4 + Math.floor(Math.random() * 8);
    for (let i = 0; i < n && i < picks.length; i++) {
      const m = picks[i];
      const c = 1 + Math.floor(Math.random() * 6);
      state.votes[m] = c;
      state.firstVote.push(m);
    }
    if (state.mode === "multi") {
      const mineN = 1 + Math.floor(Math.random() * 3);
      state.mineSet = new Set(votedOrder().slice(0, mineN));
      state.mine = [...state.mineSet][0] || null;
    } else {
      state.mine = votedOrder()[0] || null;
      state.mineSet = state.mine ? new Set([state.mine]) : new Set();
    }
    paint();
  };

  const topMap = () => votedOrder()[0] || state.maps[0];

  const reveal = () => {
    state.reveal = true;
    state.winner = topMap();
    // Mirror VoteHud.ApplyRevealPaint: reveal the whole catalog, unfiltered and open.
    state.type = 0;
    state.sort = -1;
    document.getElementById("Vote")?.classList.remove("is-minimized");
    paint();
  };

  const setMode = (mode) => {
    if (mode !== "single" && mode !== "multi") return;
    state.mode = mode;
    state.reveal = false;
    state.winner = null;
    if (mode === "single" && state.mineSet.size > 1) {
      // Keep highest-voted selected map as the single vote; drop the rest from mineSet only.
      const keep = votedOrder().find((m) => state.mineSet.has(m)) || [...state.mineSet][0];
      for (const m of [...state.mineSet]) {
        if (m !== keep) {
          bump(m, -1);
          state.mineSet.delete(m);
        }
      }
      state.mine = keep || null;
    }
    syncModeButtons();
    paint();
  };

  const syncModeButtons = () => {
    bar.querySelectorAll("[data-mode]").forEach((btn) => {
      btn.classList.toggle("is-active", btn.getAttribute("data-mode") === state.mode);
    });
    const note = bar.querySelector(".pv-note");
    if (note) {
      note.textContent =
        state.mode === "multi"
          ? "Multi: catalog +1 · voted strip −1"
          : "Single: one personal vote (CS2 default)";
    }
  };

  const setBrowseOpen = (open) => {
    document.getElementById("Vote")?.classList.toggle("pv-closed", !open);
    bar.querySelector('[data-act="open"]')?.classList.toggle("is-active", open);
    bar.querySelector('[data-act="close"]')?.classList.toggle("is-active", !open);
  };

  const seedFavorites = () => {
    state.favorites = new Set(
      ["de_dust2", "de_mirage", "cs_office"].filter((m) => state.maps.includes(m))
    );
    state.type = 0;
    state.sort = -1;
    paint();
  };

  const clearFavorites = () => {
    state.favorites.clear();
    paint();
  };

  const bar = document.createElement("div");
  bar.className = "pv-bar";
  bar.innerHTML = BROWSE
    ? `
      <button type="button" data-act="open">Open</button>
      <button type="button" data-act="close">Close</button>
      <span class="pv-sep" aria-hidden="true"></span>
      <button type="button" data-act="seed-fav">Seed favorites</button>
      <button type="button" data-act="clear-fav">Clear favorites</button>
      <span class="pv-note">Browse: filters and favorites only</span>
      <a href="vote.html">Vote preview</a>
    `
    : `
      <button type="button" data-act="clear">Clear votes</button>
      <button type="button" data-act="randomize">Randomize</button>
      <button type="button" data-act="seed">Seed demo</button>
      <button type="button" data-act="reveal">Reveal winner</button>
      <span class="pv-sep" aria-hidden="true"></span>
      <button type="button" data-mode="single">Single vote</button>
      <button type="button" data-mode="multi">Multi select</button>
      <span class="pv-note"></span>
      <a href="maps.html">Map browser preview</a>
    `;
  document.body.appendChild(bar);
  bar.addEventListener("click", (ev) => {
    const t = ev.target;
    if (!(t instanceof HTMLElement)) return;
    const act = t.getAttribute("data-act");
    const mode = t.getAttribute("data-mode");
    if (act === "open") setBrowseOpen(true);
    if (act === "close") setBrowseOpen(false);
    if (act === "seed-fav") seedFavorites();
    if (act === "clear-fav") clearFavorites();
    if (act === "clear") clearVotes();
    if (act === "randomize") randomize();
    if (act === "seed") seedVotes();
    if (act === "reveal") reveal();
    if (mode) setMode(mode);
  });

  if (BROWSE) {
    seedFavorites();
    setBrowseOpen(true);
  } else {
    seedVotes();
    syncModeButtons();
  }
})();
