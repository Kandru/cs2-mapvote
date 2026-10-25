(() => {
  const SLOT = (() => {
    let n = 0;
    document.querySelectorAll('[id^="mv-"]').forEach((el) => {
      const m = /^mv-(\d+)$/.exec(el.id);
      if (m) n = Math.max(n, Number(m[1]) + 1);
    });
    return n || 50;
  })();

  // Prefer maps that have screenshots under preview/maps/*.webp
  const MAPS = [
    "de_dust2","de_mirage","de_inferno","de_nuke","de_overpass","de_ancient","de_anubis","de_vertigo",
    "cs_office","cs_italy","ar_baggage","ar_shoots","de_cache","de_train","cs_agency","cs_assault",
    "cs_militia","de_basalt","de_brewery","de_edin","de_palacio","de_whistle","de_dogtown",
    "de_jura","de_golden","de_palais","de_thera","de_mills","de_assembly","de_boulder",
    "de_canals","de_lake","de_safehouse","de_shortdust","de_tuscan","de_cbble","cs_alpine",
    "cs_shelter","ar_pool_day","de_debris","de_eldorado","de_fachwerk","de_grail","de_memento",
    "de_poseidon","de_rooftop","de_sanctum","de_stronghold","de_sugarcane","de_transit"
  ].slice(0, SLOT);

  const stem = (map) => map.replace(/[^a-z0-9_]/gi, "_").toLowerCase();

  const applyShot = (el, map) => {
    const s = stem(map);
    el.className = "MapShot";
    el.style.backgroundImage = `url("maps/${s}.webp")`;
    el.style.backgroundSize = "100% 100%";
    el.style.backgroundPosition = "50% 50%";
    el.style.backgroundRepeat = "no-repeat";
    // Fallback if the webp 404s
    const probe = new Image();
    probe.onerror = () => {
      el.style.backgroundImage = 'url("maps/default.webp")';
    };
    probe.src = `maps/${s}.webp`;
  };

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    firstVote: [],
    mine: null,
    reveal: false,
    winner: null,
  };

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const clearTile = (tileId, shotId, voteVar, nameVar) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    tile.classList.add("is-empty");
    tile.classList.remove("is-mine", "is-winner");
    shot.className = "MapShot";
    shot.style.backgroundImage = "";
    setVar(voteVar, "");
    setVar(nameVar, "");
  };

  const paintTile = (tileId, shotId, voteVar, nameVar, map) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    const count = state.votes[map] || 0;
    tile.classList.remove("is-empty");
    tile.classList.toggle("is-mine", state.mine === map);
    tile.classList.toggle("is-winner", state.winner === map);
    applyShot(shot, map);
    setVar(voteVar, String(count));
    setVar(nameVar, map);
  };

  const votedOrder = () => {
    const alive = state.firstVote.filter((m) => (state.votes[m] || 0) > 0);
    return alive
      .map((m, i) => ({ m, c: state.votes[m] || 0, i }))
      .sort((a, b) => b.c - a.c || a.i - b.i)
      .map((x) => x.m);
  };

  const paint = () => {
    const voted = votedOrder();
    const row = document.getElementById("mv-voted-row");
    if (row) row.classList.toggle("is-empty", voted.length === 0);

    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, `vn${i}`, voted[i]);
      } else {
        clearTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, `vn${i}`);
      }

      if (i < state.maps.length) {
        paintTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, `n${i}`, state.maps[i]);
      } else {
        clearTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, `n${i}`);
      }
    }
  };

  const cast = (map) => {
    if (state.reveal || !map) return;
    const prev = state.mine;
    if (prev === map) return;
    if (prev) {
      state.votes[prev] = Math.max(0, (state.votes[prev] || 0) - 1);
      if (state.votes[prev] === 0) {
        state.firstVote = state.firstVote.filter((m) => m !== prev);
      }
    }
    state.mine = map;
    const next = (state.votes[map] || 0) + 1;
    state.votes[map] = next;
    if (next === 1) state.firstVote.push(map);
    paint();
  };

  const parseButton = (id) => {
    if (!id) return null;
    if (id.startsWith("mv-voted-")) {
      const i = Number(id.slice("mv-voted-".length));
      const order = votedOrder();
      return Number.isInteger(i) && i >= 0 && i < order.length ? order[i] : null;
    }
    if (/^mv-\d+$/.test(id)) {
      const i = Number(id.slice(3));
      return Number.isInteger(i) && i >= 0 && i < state.maps.length ? state.maps[i] : null;
    }
    return null;
  };

  document.getElementById("Vote")?.addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[id]");
    if (!btn) return;
    cast(parseButton(btn.id));
  });

  const seedVotes = () => {
    ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "cs_office"].forEach((m, idx) => {
      if (!state.maps.includes(m)) return;
      state.votes[m] = idx === 0 ? 3 : idx === 1 ? 2 : 1;
      state.firstVote.push(m);
    });
    state.mine = "de_dust2";
  };

  const topMap = () => votedOrder()[0] || state.maps[0];

  const reveal = () => {
    state.reveal = true;
    state.winner = topMap();
    paint();
  };

  const reset = () => {
    state.votes = Object.fromEntries(state.maps.map((m) => [m, 0]));
    state.firstVote = [];
    state.mine = null;
    state.reveal = false;
    state.winner = null;
    seedVotes();
    paint();
  };

  const bar = document.createElement("div");
  bar.className = "pv-bar";
  bar.innerHTML = `
    <button type="button" data-act="reset">Reset demo</button>
    <button type="button" data-act="reveal">Reveal winner</button>
    <span class="pv-note">CS2 vote cards · voted row + 10-across grid · maps/*.webp</span>
  `;
  document.body.appendChild(bar);
  bar.addEventListener("click", (ev) => {
    const act = ev.target.getAttribute("data-act");
    if (act === "reset") reset();
    if (act === "reveal") reveal();
  });

  seedVotes();
  paint();
})();
