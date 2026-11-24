#!/usr/bin/env python3
"""Generate the current Panorama layout XML and its generated CSS ladders.

LAYOUT CONTRACT - the list the C# view codes against. Spelled once in VoteIds.cs.
"G" = write globally (same for every viewer), "P" = write per player. Never both on one name.
Every pool slot is authored in its default state; XML default = C# default.

Root
  #Vote                    classes: is-off (P, XML on), is-fading (P), is-minimized (P),
                           view group view-vote | view-reveal | view-browse (P, XML view-vote).
                           Reveal is a root view: grid dimmed, rating dimmed, winner card shown.
                           Browse (!maps): timer and status check hidden; ranking, vote badges
                           and sort row live (same renderer as the vote).
Top bar
  mv_title      (G) title text                      "Stimme für die nächste Map" / browse "Karten · 152" (whole list)
  #mv-timer               is-urgent (P, last 5 s; red text + pulse, the only infinite animation)
  mv_time       (G) countdown text, at most 1x/s     "0:08"
  #mv-timer-fill          span-3 … span-90 (P, set first), is-running (P, next tick). Reset: remove both.
  #mv-timer-fill-reveal   same classes, used only in the reveal: a fresh panel, because reversing a still
                          running drain refills at the old pace (after a late client start, e.g. a reconnect).
  #mv-minimize  Button    hide/show toggle
  mv_hide       (P) its label                        "Ausblenden" / "Einblenden"
Status
  #mv-status              is-self (P) when the viewer has voted (accent + check)
  mv_status     (P) "Deine Stimme: Mirage – klick eine andere Map, um zu wechseln" / hint
Current map (also in browse)
  #mv-current             is-off (G, XML off: visible)
  mv_cur_tag    (G) "Aktuelle Map"
  mv_cur_name   (G) map display name (wrap in NBSP, B11)
  #mv-current-shot        one mv-map-* class (G, XML none -> default image shows through)
  #mv-current-fav Button  is-active (P) = favourite
  #mv-rate                is-disabled (P) without an authorised SteamID
  mv_rate_q     (G) "Wie war die Map?"
  #mv-like / #mv-dislike  Buttons, is-active (P) = own rating (like: success surface, dislike: danger) + check
  mv_like, mv_dislike     (G) labels "Gefällt mir" / "Gefällt mir nicht"
  mv_like_n, mv_dislike_n (G) counters "12" / "3"
Ranking (pool RANK_SLOTS = 6)
  mv_rank_title (G) "Vorne"
  #mv-rank-empty          is-off (G, XML off: visible = no votes yet)
  mv_rank_empty (G) "Noch keine Stimmen – wähl unten eine Map"
  #mv-rank-{i}  Button    is-off (G, XML on); click = vote for that row's map
  #mv-rank-shot-{i}       mv-map-* (G)
  #mv-rank-fill-{i}       share ladder p0, p10 … p100 (G, XML p0)
  rn{i}, rv{i}  (G) name / vote count
  #mv-rank-self-{i}       is-self (P) = own vote (accent tint + check + mv_self text)
  mv_self       (G) "Deine Stimme"
  Rank numbers 1-6 are XML literals.
Filters
  #mv-filters             is-off (G, XML off: visible)
  #mv-ft-{i}    Button    is-off (G, XML on), is-active (P), is-disabled (G); i < FilterTypeSlots
  ft{i}         (G) label with count "Bomb 34"
  #mv-fav-only  Button    is-active (P), is-disabled (P, no favourites)
  mv_favonly    (G) "Favoriten"
  #mv-fs-{i}    Button    sort chips, browse only: is-off (G, XML on), is-active (G - order is global)
  fs{i}         (G) label
Grid (pool GRID_SLOTS = VoteIds.SlotCount)
  #mv-slot-{i}            is-off (P, XML on) = filter/visibility, is-self (P) = own vote
  #mv-{i}       Button    vote group votes-none | votes-some (G, XML votes-none); badge only when > 0;
                          rating ladder p0, p10 … p100 (G, XML none = unrated: grey bar) = like share of
                          all ratings, mixed ratings clamped to p10 … p90 (VoteView.RatingStep)
  RateBar                 thin strip on the shot's bottom edge: grey, or red track + green like share
  #mv-shot-{i}            mv-map-* (G)
  #mv-fav-{i}   Button    is-active (P) = favourite (filled star; outline star only on hover)
  v{i}, n{i}    (G) vote count / name
  CardTopLeft             rating pill + own-vote check (both overlay the shot's top-left)
  ln{i}, dn{i}  (G) positive / negative rating counts, compact form (999, 1.2k, 12k)
  ad{i}         (P) date the map was added (maps.*.added, UTC day), lang hud.added_format; browse only
                    (row collapsed in the vote, never written there); empty = unknown
  #mv-empty               is-off (P, XML on)
  mv_empty      (P) "Keine Map passt zu diesem Filter"
Reveal (visible with view-reveal)
  mv_win_tag    (G) "Nächste Map"
  #mv-win-shot            mv-map-* (G)
  mv_win_name   (G) name
  mv_win_votes  (G) "4 Stimmen"
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mapvote_layout import (
    HUD_VCSS_NAME,
    MAPS_VCSS_NAME,
    FILTER_SORT_SLOTS,
    FILTER_TYPE_SLOTS,
    GRID_SLOTS,
    RANK_SLOTS,
    SHARE_STEP,
    SPAN_MAX,
    SPAN_MIN,
    PANORAMA_VERSION,
    VOTE_XML_NAME,
)

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "workshop" / "content" / "panorama" / "layout" / "custom_game" / "mapvote" / VOTE_XML_NAME
HUD_VCSS = ROOT / "workshop" / "content" / "panorama" / "styles" / "custom_game" / "mapvote" / HUD_VCSS_NAME
CREDIT = "made by kandru.net - visit github.com/kandru/cs2-mapvote"
LAYOUT_TAG = (
    f"{PANORAMA_VERSION} grid={GRID_SLOTS} rank={RANK_SLOTS} "
    f"filter_types={FILTER_TYPE_SLOTS} filter_sorts={FILTER_SORT_SLOTS} "
    f"span={SPAN_MIN}-{SPAN_MAX} share_step={SHARE_STEP}"
)


class Xml:
    """Tab-indented, one element per line; attribute order id → class → hittest → text (§9.2)."""

    def __init__(self) -> None:
        self.lines: list[str] = []
        self.depth = 0

    @staticmethod
    def _attrs(tag: str, id_: str | None, cls: str | None, text: str | None, hit: bool) -> str:
        parts = []
        if id_:
            parts.append(f'id="{id_}"')
        if cls:
            parts.append(f'class="{cls}"')
        if tag != "Button" and not hit:  # hit=True only for wheel/shield panels
            parts.append('hittest="false"')
        if text is not None:
            parts.append(f'text="{text}"')
        return " ".join(parts)

    def leaf(self, tag: str, id_: str | None = None, cls: str | None = None, text: str | None = None) -> None:
        self.lines.append("\t" * self.depth + f"<{tag} {self._attrs(tag, id_, cls, text, False)} />")

    def open(self, tag: str, id_: str | None = None, cls: str | None = None, hit: bool = False) -> None:
        self.lines.append("\t" * self.depth + f"<{tag} {self._attrs(tag, id_, cls, None, hit)}>")
        self.depth += 1

    def close(self, tag: str) -> None:
        self.depth -= 1
        self.lines.append("\t" * self.depth + f"</{tag}>")


def shot(x: Xml, frame_cls: str, shot_id: str) -> None:
    # Default image under the id'd overlay: no mv-map-* class → the default shows through.
    x.open("Panel", cls=frame_cls)
    x.leaf("Panel", cls="MapShot mv-map-default")
    x.leaf("Panel", id_=shot_id, cls="MapShot")


def chip(x: Xml, chip_id: str, var: str, cls: str = "FilterChip is-off") -> None:
    x.open("Button", chip_id, cls)
    x.leaf("Label", cls="ChipLabel", text=f"{{s:{var}}}")
    x.close("Button")


def top_bar(x: Xml) -> None:
    x.open("Panel", cls="TopBar")
    x.leaf("Label", cls="VoteTitle", text="{s:mv_title}")
    x.open("Panel", "mv-timer", "Timer")
    x.leaf("Label", cls="TimerText", text="{s:mv_time}")
    x.open("Panel", cls="TimerTrack")
    x.leaf("Panel", "mv-timer-fill", "TimerFill")
    x.leaf("Panel", "mv-timer-fill-reveal", "TimerFill TimerFillReveal")
    x.close("Panel")
    x.close("Panel")
    x.open("Button", "mv-minimize", "HideBtn")
    x.leaf("Label", cls="HideLabel", text="{s:mv_hide}")
    x.close("Button")
    x.close("Panel")


def rate_button(x: Xml, kind: str) -> None:
    x.open("Button", f"mv-{kind}", f"RateBtn {kind.capitalize()}Btn")
    x.leaf("Panel", cls=f"RateIcon ico-{kind}")
    x.leaf("Label", cls="RateLabel", text=f"{{s:mv_{kind}}}")
    x.leaf("Label", cls="RateCount", text=f"{{s:mv_{kind}_n}}")
    x.leaf("Panel", cls="RateCheck")
    x.close("Button")


def current_card(x: Xml) -> None:
    x.open("Panel", "mv-current", "CurrentCard")
    x.open("Panel", cls="CurrentInfo")
    x.open("Panel", cls="CurrentHead")
    x.leaf("Label", cls="CardTag", text="{s:mv_cur_tag}")
    x.open("Button", "mv-current-fav", "FavBtn CurrentFav")
    x.leaf("Panel", cls="FavIcon")
    x.close("Button")
    x.close("Panel")
    shot(x, "CurrentImage", "mv-current-shot")
    x.close("Panel")
    x.leaf("Label", cls="CurrentName", text="{s:mv_cur_name}")
    x.close("Panel")
    x.open("Panel", "mv-rate", "RateArea")
    x.leaf("Label", cls="RateQuestion", text="{s:mv_rate_q}")
    rate_button(x, "like")
    rate_button(x, "dislike")
    x.close("Panel")
    x.close("Panel")


def ranking(x: Xml) -> None:
    x.open("Panel", "mv-rank", "Ranking")
    x.leaf("Label", cls="RankTitle", text="{s:mv_rank_title}")
    x.leaf("Label", "mv-rank-empty", "RankEmpty", "{s:mv_rank_empty}")
    for i in range(RANK_SLOTS):
        x.open("Button", f"mv-rank-{i}", "RankRow is-off")
        x.open("Panel", f"mv-rank-self-{i}", "RankSelf")
        x.open("Panel", cls="RankSelfMark")
        x.leaf("Panel", cls="CheckIcon")
        x.leaf("Label", cls="RankSelfLabel", text="{s:mv_self}")
        x.close("Panel")
        x.close("Panel")
        x.open("Panel", cls="RankLine")
        x.leaf("Label", cls="RankPos", text=str(i + 1))
        shot(x, "RankImage", f"mv-rank-shot-{i}")
        x.close("Panel")
        x.leaf("Label", cls="RankName", text=f"{{s:rn{i}}}")
        x.open("Panel", cls="RankTrack")
        x.leaf("Panel", f"mv-rank-fill-{i}", "RankFill p0")
        x.close("Panel")
        x.leaf("Label", cls="RankVotes", text=f"{{s:rv{i}}}")
        x.leaf("Panel", cls="RankSelfGap")
        x.close("Panel")
        x.close("Button")
    x.close("Panel")


def filters(x: Xml) -> None:
    # Hit-enabled: shields the chips from scrolled grid cards underneath.
    x.open("Panel", "mv-filters", "FilterBar", hit=True)
    x.open("Panel", cls="FilterRow")
    for i in range(FILTER_TYPE_SLOTS):
        chip(x, f"mv-ft-{i}", f"ft{i}")
    x.leaf("Panel", cls="FilterGap")
    x.open("Button", "mv-fav-only", "FilterChip FavChip")
    x.leaf("Panel", cls="FavIcon")
    x.leaf("Label", cls="ChipLabel", text="{s:mv_favonly}")
    x.close("Button")
    x.close("Panel")
    x.open("Panel", cls="SortRow")
    for i in range(FILTER_SORT_SLOTS):
        chip(x, f"mv-fs-{i}", f"fs{i}")
    x.close("Panel")
    x.close("Panel")


def grid_slot(x: Xml, i: int) -> None:
    x.open("Panel", f"mv-slot-{i}", "MapSlot is-off", hit=True)
    x.open("Button", f"mv-{i}", "MapCard votes-none")
    shot(x, "CardImage", f"mv-shot-{i}")
    x.leaf("Label", cls="VoteBadge", text=f"{{s:v{i}}}")
    x.open("Panel", cls="CardTopLeft")
    x.open("Panel", cls="CardRating")
    x.leaf("Panel", cls="RatingIcon ico-like")
    x.leaf("Label", cls="RatingCount LikeCount", text=f"{{s:ln{i}}}")
    x.leaf("Panel", cls="RatingIcon ico-dislike")
    x.leaf("Label", cls="RatingCount DislikeCount", text=f"{{s:dn{i}}}")
    x.close("Panel")
    x.open("Panel", cls="SelfBadge")
    x.leaf("Panel", cls="CheckIcon")
    x.close("Panel")
    x.close("Panel")
    # Rating strip along the shot's bottom edge: grey until the tile gets a p* class.
    x.open("Panel", cls="RateBar")
    x.leaf("Panel", cls="RateFill")
    x.close("Panel")
    x.close("Panel")
    x.leaf("Label", cls="CardName", text=f"{{s:n{i}}}")
    x.leaf("Label", cls="CardDate", text=f"{{s:ad{i}}}")
    x.close("Button")
    x.leaf("Panel", cls="CardFrame")
    x.open("Button", f"mv-fav-{i}", "FavBtn CardFav")
    x.leaf("Panel", cls="FavIcon")
    x.close("Button")
    x.close("Panel")


def grid(x: Xml) -> None:
    x.open("Panel", cls="GridHost")
    x.open("Panel", "mv-catalog", "MapCatalog", hit=True)
    for i in range(GRID_SLOTS):
        grid_slot(x, i)
    x.close("Panel")
    x.leaf("Panel", cls="GridFade")
    x.leaf("Label", "mv-empty", "MapEmpty is-off", "{s:mv_empty}")
    x.open("Panel", cls="RevealCard")
    x.leaf("Label", cls="RevealTag", text="{s:mv_win_tag}")
    shot(x, "RevealImage", "mv-win-shot")
    x.close("Panel")
    x.leaf("Label", cls="RevealName", text="{s:mv_win_name}")
    x.leaf("Label", cls="RevealVotes", text="{s:mv_win_votes}")
    x.close("Panel")
    x.close("Panel")


def build() -> str:
    x = Xml()
    x.lines += [
        f"<!-- AUTO-GENERATED by tools/generate_vote_xml.py - do not edit by hand. Contract: see that file. -->",
        "<root>",
        "\t<styles>",
        '\t\t<include src="s2r://panorama/styles/csgostyles.vcss" />',
        f'\t\t<include src="s2r://panorama/styles/custom_game/mapvote/{HUD_VCSS_NAME}" />',
        f'\t\t<include src="s2r://panorama/styles/custom_game/mapvote/{MAPS_VCSS_NAME}" />',
        "\t</styles>",
        "",
        f"\t<!-- MapVote:mapvote {LAYOUT_TAG} -->",
    ]
    x.depth = 1
    x.open("Panel", cls="HudScreen")
    x.open("Panel", "Vote", "VoteRoot is-off view-vote")
    x.leaf("Panel", cls="mv-glass")
    x.leaf("Panel", cls="mv-scrim")
    x.open("Panel", cls="VoteColumn")
    top_bar(x)
    x.open("Panel", cls="VoteBody")
    x.open("Panel", "mv-status", "StatusLine")
    x.leaf("Panel", cls="CheckIcon StatusCheck")
    x.leaf("Label", cls="StatusText", text="{s:mv_status}")
    x.close("Panel")
    x.open("Panel", cls="HeadRow")
    current_card(x)
    ranking(x)
    x.close("Panel")
    filters(x)
    grid(x)
    x.close("Panel")
    x.open("Panel", cls="CreditBar")
    x.leaf("Label", cls="CreditLabel", text=CREDIT)
    x.close("Panel")
    x.close("Panel")
    x.close("Panel")
    x.close("Panel")
    x.lines += ["</root>", ""]
    return "\n".join(x.lines)


def _replace_block(text: str, name: str, lines: list[str]) -> str:
    begin, end = f"/* BEGIN {name} */", f"/* END {name} */"
    pattern = re.escape(begin) + r".*?" + re.escape(end)
    if re.search(pattern, text, flags=re.S) is None:
        raise SystemExit(f"{HUD_VCSS.name}: missing {begin} … {end}")
    return re.sub(pattern, lambda _: "\n".join([begin, *lines, end]), text, count=1, flags=re.S)


def ladders(text: str) -> str:
    """Countdown span, ranking share and tile rating ladders, generated from the pool constants."""
    spans = [f".TimerFill.span-{s} {{ transition-duration: {s}.0s; }}" for s in range(SPAN_MIN, SPAN_MAX + 1)]
    steps = range(0, 101, SHARE_STEP)
    shares = [f".RankFill.p{p} {{ clip: rect( 0%, {p}%, 100%, 0% ); }}" for p in steps]
    # Tile rating bar: any p* class on the card = rated (red track); the green fill clip is the like share.
    rating = [
        ",\n".join(f".MapCard.p{p} .RateBar" for p in steps), "{", "\tbackground-color: #e05a5a;", "}",
        *(f".MapCard.p{p} .RateFill {{ clip: rect( 0%, {p}%, 100%, 0% ); }}" for p in steps),
    ]
    text = _replace_block(_replace_block(text, "span-ladder", spans), "share-ladder", shares)
    return _replace_block(text, "rating-ladder", rating)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="exit 1 if the committed files differ from the generated output")
    args = parser.parse_args()
    xml = build()
    css_now = HUD_VCSS.read_text(encoding="utf-8").replace("\r\n", "\n") if HUD_VCSS.is_file() else None
    if css_now is None:
        print(f"missing {HUD_VCSS.relative_to(ROOT)}", file=sys.stderr)
        return 1
    css = ladders(css_now)
    if args.check:
        stale = []
        if not OUT.is_file() or OUT.read_text(encoding="utf-8").replace("\r\n", "\n") != xml:
            stale.append(str(OUT.relative_to(ROOT)))
        if css != css_now:
            stale.append(str(HUD_VCSS.relative_to(ROOT)))
        if stale:
            print(f"{', '.join(stale)} out of date; run tools/generate_vote_xml.py", file=sys.stderr)
            return 1
        print(f"{OUT.relative_to(ROOT)} + {HUD_VCSS.name} up to date")
        return 0
    OUT.write_text(xml, encoding="utf-8", newline="\n")
    HUD_VCSS.write_text(css, encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(ROOT)} ({LAYOUT_TAG})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
