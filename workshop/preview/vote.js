(() => {
  const SLOT = (() => {
    let n = 0;
    document.querySelectorAll('[id^="mv-"]').forEach((el) => {
      const m = /^mv-(\d+)$/.exec(el.id);
      if (m) n = Math.max(n, Number(m[1]) + 1);
    });
    return n || 50;
  })();

  const MAPS = [
    "de_dust2","de_mirage","de_inferno","de_nuke","de_overpass","de_ancient","de_anubis","de_vertigo",
    "cs_office","cs_italy","ar_baggage","ar_shoots","de_cache","de_train","cs_agency","cs_assault",
    "cs_militia","de_basalt","de_boyard","de_edin","de_palacio","de_whistle","de_brewery","de_dogtown",
    "de_graffiti","de_jura","de_golden","de_seaside","gd_cbble",
    "de_dust2_x","de_mirage_x","de_inferno_x","de_nuke_x","de_overpass_x","de_ancient_x","de_anubis_x",
    "de_vertigo_x","cs_office_x","cs_italy_x","ar_baggage_x","ar_shoots_x","de_cache_x","de_train_x",
    "cs_agency_x","cs_assault_x","cs_militia_x","de_basalt_x","de_boyard_x","de_edin_x","de_palacio_x"
  ].slice(0, SLOT);

  const stem = (map) => map.replace(/[^a-z0-9_]/gi, "_").toLowerCase();

  const hasMapClass = (s) => {
    const probe = document.createElement("div");
    probe.className = `mv-map-${s}`;
    document.body.appendChild(probe);
    const bg = getComputedStyle(probe).backgroundImage;
    probe.remove();
    return bg && bg !== "none";
  };

  const mapClass = (map) => {
    const s = stem(map);
    return hasMapClass(s) ? `mv-map-${s}` : "mv-map-default";
  };

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    firstVote: [],
    mine: null,
    timer: 45,
    reveal: false,
    bumpSide: false,
    lastStrip: [],
  };

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const clearTile = (tileId, shotId, varName) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    tile.classList.add("is-empty");
    tile.classList.remove("is-voted", "is-mine", "bump-a", "bump-b");
    shot.className = "MapShot mv-map-default";
    setVar(varName, "");
  };

  const paintTile = (tileId, shotId, varName, map, mine, bump) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    const count = state.votes[map] || 0;
    tile.classList.remove("is-empty");
    tile.classList.toggle("is-voted", count > 0);
    tile.classList.toggle("is-mine", mine === map);
    tile.classList.toggle("bump-a", !!bump && state.bumpSide);
    tile.classList.toggle("bump-b", !!bump && !state.bumpSide);
    shot.className = `MapShot ${mapClass(map)}`;
    setVar(varName, count > 0 ? String(count) : "");
  };

  const votedOrder = () => {
    const alive = state.firstVote.filter((m) => (state.votes[m] || 0) > 0);
    return alive
      .map((m, i) => ({ m, c: state.votes[m] || 0, i }))
      .sort((a, b) => b.c - a.c || a.i - b.i)
      .map((x) => x.m);
  };
  const gridOrder = () => state.maps;

  const paint = () => {
    const voted = votedOrder();
    const grid = gridOrder();
    const row = document.getElementById("mv-voted-row");
    if (row) row.classList.toggle("is-empty", voted.length === 0);

    const bumps = voted.map((m, i) => {
      const prev = state.lastStrip[i];
      return !prev || prev.map !== m || prev.votes !== (state.votes[m] || 0);
    });
    if (bumps.some(Boolean)) state.bumpSide = !state.bumpSide;

    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, voted[i], state.mine, bumps[i]);
      } else {
        clearTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`);
      }

      if (i < grid.length) paintTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, grid[i], state.mine, false);
      else clearTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`);
    }

    state.lastStrip = voted.map((m) => ({ map: m, votes: state.votes[m] || 0 }));
    setVar("timer", state.reveal ? "" : String(state.timer));
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
      const order = gridOrder();
      return Number.isInteger(i) && i >= 0 && i < order.length ? order[i] : null;
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

  const reveal = () => {
    state.reveal = true;
    const root = document.getElementById("Vote");
    const winner = document.getElementById("mv-winner");
    const shot = document.getElementById("mv-winner-shot");
    root?.classList.add("is-reveal");
    winner?.classList.remove("is-empty");
    const top = votedOrder()[0] || state.maps[0];
    if (shot) shot.className = `WinnerShot ${mapClass(top)}`;
    setVar("winner_name", top);
    setVar("winner_votes", String(state.votes[top] || 0));
    setVar("timer", "");
    paint();
  };

  const reset = () => {
    state.votes = Object.fromEntries(state.maps.map((m) => [m, 0]));
    state.firstVote = [];
    state.lastStrip = [];
    state.mine = null;
    state.timer = 45;
    state.reveal = false;
    document.getElementById("Vote")?.classList.remove("is-reveal");
    document.getElementById("mv-winner")?.classList.add("is-empty");
    setVar("title", "Map Vote");
    setVar("winner_name", "");
    setVar("winner_votes", "");
    seedVotes();
    paint();
  };

  // Controls
  const bar = document.createElement("div");
  bar.className = "pv-bar";
  bar.innerHTML = `
    <button type="button" data-act="reset">Reset demo</button>
    <button type="button" data-act="reveal">Reveal winner</button>
    <span class="pv-note">Synced from vote.xml + hud.vcss. Strip = votes sorted; grid keeps all maps with the same vote overlay.</span>
  `;
  document.body.appendChild(bar);
  bar.addEventListener("click", (ev) => {
    const act = ev.target.getAttribute("data-act");
    if (act === "reset") reset();
    if (act === "reveal") reveal();
  });

  setVar("title", "Map Vote");
  seedVotes();
  paint();
  setInterval(() => {
    if (state.reveal || state.timer <= 0) return;
    state.timer -= 1;
    setVar("timer", String(state.timer));
  }, 1000);
})();
