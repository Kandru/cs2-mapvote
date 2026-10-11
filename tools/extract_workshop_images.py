#!/usr/bin/env python3
"""Extract the best map image from workshop / map VPKs into the MapVote Panorama addon."""
from __future__ import annotations

import argparse
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "workshop" / "content" / "panorama" / "images" / "custom_game" / "mapvote" / "maps"
HUD_VCSS = ROOT / "workshop" / "content" / "panorama" / "styles" / "custom_game" / "mapvote" / "hud.vcss"

CSS_BEGIN = "/* BEGIN mv-map-palette */"
CSS_END = "/* END mv-map-palette */"

# Final HUD thumb size: 350px wide, 16:9 (matches .MapTile).
TARGET_WIDTH = 350
TARGET_HEIGHT = 197  # round(350 * 9 / 16)
DEFAULT_STEM = "default"

ENTRY_FMT = struct.Struct("<IHHIIH")
assert ENTRY_FMT.size == 18

DENIED_PATH_PARTS = (
    "materials/",
    "lightmaps/",
    "cubemaps/",
    "skybox/",
)
DENIED_NAME_PARTS = ("_normal", "_rough", "_metal", "_mask", "_ao", "_height")

MAP_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9_]{0,48}$")


class VpkEntry:
    __slots__ = ("path", "crc", "preload", "archive", "offset", "length")

    def __init__(self, path: str, crc: int, preload: int, archive: int, offset: int, length: int):
        self.path = path
        self.crc = crc
        self.preload = preload
        self.archive = archive
        self.offset = offset
        self.length = length


def parse_vpk_dir(path: Path) -> tuple[int, bytes, list[VpkEntry]]:
    with path.open("rb") as f:
        header = f.read(28)
        if len(header) < 12:
            raise ValueError(f"short VPK header: {path}")
        sig, ver, tree_size = struct.unpack_from("<III", header, 0)
        if sig != 0x55AA1234:
            raise ValueError(f"not a VPK: {path}")
        if ver >= 2:
            f.seek(28)
        else:
            f.seek(12)
        tree = f.read(tree_size)
    return ver, tree, parse_tree(tree)


def parse_tree(tree: bytes) -> list[VpkEntry]:
    i = 0
    entries: list[VpkEntry] = []

    def cstr() -> str:
        nonlocal i
        end = tree.find(b"\x00", i)
        if end < 0:
            raise ValueError("unterminated string in VPK tree")
        s = tree[i:end].decode("latin1")
        i = end + 1
        return s

    while i < len(tree):
        ext = cstr()
        if ext == "":
            break
        while True:
            pathn = cstr()
            if pathn == "":
                break
            while True:
                fn = cstr()
                if fn == "":
                    break
                crc, preload, archive, offset, length, term = ENTRY_FMT.unpack_from(tree, i)
                if term != 0xFFFF:
                    raise ValueError(f"bad VPK terminator in {pathn}/{fn}.{ext}")
                i += ENTRY_FMT.size + preload
                full = f"{pathn}/{fn}.{ext}" if pathn else f"{fn}.{ext}"
                entries.append(VpkEntry(full, crc, preload, archive, offset, length))
    return entries


def archive_path(dir_vpk: Path, archive_index: int) -> Path:
    # archive 0x7fff means data is in the dir/single file after the tree
    if archive_index == 0x7FFF:
        return dir_vpk
    stem = dir_vpk.name
    if stem.endswith("_dir.vpk"):
        base = stem[: -len("_dir.vpk")]
        return dir_vpk.with_name(f"{base}_{archive_index:03d}.vpk")
    return dir_vpk


def data_base_offset(dir_vpk: Path, ver: int, tree_size: int) -> int:
    return (28 if ver >= 2 else 12) + tree_size


def extract_entry(dir_vpk: Path, ver: int, tree_size: int, entry: VpkEntry, dest: Path) -> None:
    src = archive_path(dir_vpk, entry.archive)
    if not src.is_file():
        raise FileNotFoundError(f"archive missing for {entry.path}: {src}")
    with src.open("rb") as f:
        if entry.archive == 0x7FFF:
            f.seek(data_base_offset(dir_vpk, ver, tree_size) + entry.offset)
        else:
            f.seek(entry.offset)
        data = f.read(entry.length)
    if len(data) != entry.length:
        raise IOError(f"short read for {entry.path}: got {len(data)} expected {entry.length}")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(data)


def discover_vpks(root: Path) -> list[Path]:
    vpks: list[Path] = []
    if root.is_file() and root.suffix.lower() == ".vpk":
        return [root]
    for path in sorted(root.rglob("*.vpk")):
        name = path.name
        if name.endswith("_dir.vpk"):
            vpks.append(path)
        elif "_" not in name[:-4] and not any(
            path.with_name(f"{path.stem}_dir.vpk").exists() for _ in [0]
        ):
            # single-file VPK (no _000 / _dir sibling preferred)
            sibling_dir = path.with_name(f"{path.stem}_dir.vpk")
            if not sibling_dir.exists():
                vpks.append(path)
    # Also accept bare maps/*.vpk that are single archives
    return sorted(set(vpks))


def map_names_from_entries(entries: list[VpkEntry]) -> set[str]:
    names: set[str] = set()
    for e in entries:
        p = e.path.replace("\\", "/").lower()
        if p.startswith("cfg/maps/") and p.endswith(".cfg"):
            stem = Path(p).stem
            if MAP_NAME_RE.match(stem):
                names.add(stem)
        elif "/maps/" in p and p.endswith(".vmap_c"):
            stem = Path(p).stem
            if MAP_NAME_RE.match(stem):
                names.add(stem)
    return names


def is_denied(path: str) -> bool:
    low = path.lower()
    if any(part in low for part in DENIED_PATH_PARTS):
        return True
    name = Path(low).name
    return any(part in name for part in DENIED_NAME_PARTS)


def pick_image(entries: list[VpkEntry], map_name: str) -> VpkEntry | None:
    candidates = [e for e in entries if e.path.lower().endswith(".vtex_c") and not is_denied(e.path)]
    if not candidates:
        return None

    def under(prefix: str) -> list[VpkEntry]:
        return [
            e
            for e in candidates
            if e.path.replace("\\", "/").lower().startswith(prefix)
            and map_name in Path(e.path).name.lower()
        ]

    for res in ("1080p", "720p", "360p"):
        shots = under(f"panorama/images/map_icons/screenshots/{res}/")
        if shots:
            return max(shots, key=lambda e: e.length)

    radars = under("panorama/images/overheadmaps/")
    radars = [e for e in radars if "radar" in e.path.lower()]
    if radars:
        normal = [e for e in radars if "spectate" not in e.path.lower()]
        pool = normal or radars
        return max(pool, key=lambda e: e.length)
    return None


def write_vtex(stem: str) -> None:
    path = IMAGES / f"{stem}.vtex"
    path.write_text(
        f"""<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {{
            "m_name" "string" "0"
            "m_fileName" "string" "panorama/images/custom_game/mapvote/maps/{stem}.png"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
        }}
    ]

    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "RGBA8888"
    "m_bNoLod" "bool" "1"

    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {{
            "m_inputTextureArray" "string_array" [ "0" ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_outputColorSpace" "string" "srgb"
        }}
    ]
}}
""",
        encoding="utf-8",
    )


def rewrite_css(stems: list[str]) -> None:
    if not HUD_VCSS.is_file():
        raise SystemExit(f"missing stylesheet: {HUD_VCSS}")
    text = HUD_VCSS.read_text(encoding="utf-8")
    if CSS_BEGIN not in text or CSS_END not in text:
        raise SystemExit(f"stylesheet missing palette markers {CSS_BEGIN!r} / {CSS_END!r}")

    lines = [CSS_BEGIN, ""]
    for stem in stems:
        lines.append(f".mv-map-{stem}")
        lines.append("{")
        lines.append(
            f'\tbackground-image: url("s2r://panorama/images/custom_game/mapvote/maps/{stem}.vtex");'
        )
        lines.append("\tbackground-size: 100% 100%;")
        lines.append("\tbackground-position: center center;")
        lines.append("\tbackground-repeat: no-repeat;")
        lines.append("}")
        lines.append("")
    lines.append(CSS_END)
    block = "\n".join(lines)
    pattern = re.compile(re.escape(CSS_BEGIN) + r".*?" + re.escape(CSS_END), re.S)
    HUD_VCSS.write_text(pattern.sub(block, text), encoding="utf-8")


def resize_to_tile(png_path: Path) -> None:
    """Center-crop to 16:9, then scale to TARGET_WIDTH × TARGET_HEIGHT."""
    try:
        from PIL import Image
    except ImportError as ex:
        raise SystemExit("Pillow is required for image resize (pip install Pillow)") from ex

    with Image.open(png_path) as img:
        src = img.convert("RGBA")
        sw, sh = src.size
        if sw <= 0 or sh <= 0:
            raise RuntimeError(f"invalid image size {sw}x{sh}: {png_path}")

        target_ratio = TARGET_WIDTH / TARGET_HEIGHT
        src_ratio = sw / sh
        if src_ratio > target_ratio:
            # Too wide — crop sides.
            new_w = int(round(sh * target_ratio))
            left = (sw - new_w) // 2
            src = src.crop((left, 0, left + new_w, sh))
        elif src_ratio < target_ratio:
            # Too tall — crop top/bottom.
            new_h = int(round(sw / target_ratio))
            top = (sh - new_h) // 2
            src = src.crop((0, top, sw, top + new_h))

        out = src.resize((TARGET_WIDTH, TARGET_HEIGHT), Image.Resampling.LANCZOS)
        out.save(png_path, format="PNG", optimize=True)


def find_cli(explicit: str | None) -> Path:
    candidates: list[Path] = []
    if explicit:
        candidates.append(Path(explicit))
    env = os.environ.get("SOURCE2VIEWER_CLI")
    if env:
        candidates.append(Path(env))
    candidates.append(ROOT / "tools" / ".cache" / "Source2Viewer-CLI")
    which = shutil.which("Source2Viewer-CLI")
    if which:
        candidates.append(Path(which))
    for c in candidates:
        if c.is_file() and os.access(c, os.X_OK):
            return c
    raise SystemExit(
        "Source2Viewer-CLI not found. Download cli-linux-x64.zip from "
        "https://github.com/ValveResourceFormat/ValveResourceFormat/releases "
        "into tools/.cache/ or set SOURCE2VIEWER_CLI."
    )


def decompile_vtex(cli: Path, vtex_c: Path, png_out: Path) -> None:
    out_dir = vtex_c.parent
    # VRF 20 writes the PNG beside the input when -o is a directory.
    cmd = [str(cli), "-i", str(vtex_c), "-o", str(out_dir), "-d"]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(
            f"Source2Viewer-CLI failed for {vtex_c.name}:\n{proc.stdout}\n{proc.stderr}"
        )
    pngs = list(out_dir.rglob("*.png"))
    if not pngs:
        raise RuntimeError(f"no PNG produced for {vtex_c.name}; stdout:\n{proc.stdout}")
    best = max(pngs, key=lambda p: p.stat().st_size)
    png_out.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(best, png_out)
    resize_to_tile(png_out)


def process_vpk(cli: Path, vpk: Path, written: dict[str, Path]) -> None:
    print(f"[images] scanning {vpk}")
    ver, tree, entries = parse_vpk_dir(vpk)
    tree_size = len(tree)
    maps = map_names_from_entries(entries)
    if not maps:
        # Fallback: filename stem for maps/de_foo.vpk
        stem = vpk.stem
        if stem.endswith("_dir"):
            stem = stem[: -len("_dir")]
        # Prefer map names from paths even if empty — skip unknown packages
        print(f"[images]  no map names in {vpk.name}, skip")
        return

    for map_name in sorted(maps):
        entry = pick_image(entries, map_name)
        if entry is None:
            print(f"[images]  {map_name}: no screenshot/radar")
            continue
        with tempfile.TemporaryDirectory(prefix="mapvote-extract-") as tmp:
            vtex_c = Path(tmp) / Path(entry.path).name
            extract_entry(vpk, ver, tree_size, entry, vtex_c)
            png = IMAGES / f"{map_name}.png"
            decompile_vtex(cli, vtex_c, png)
            write_vtex(map_name)
            written[map_name] = png
            print(f"[images]  {map_name} ← {entry.path} ({entry.length} bytes)")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workshop_dir", type=Path, help="Folder of workshop packages or map VPKs")
    parser.add_argument(
        "--cli",
        default=os.environ.get("SOURCE2VIEWER_CLI"),
        help="Path to Source2Viewer-CLI",
    )
    args = parser.parse_args()
    root = args.workshop_dir.expanduser().resolve()
    if not root.exists():
        print(f"path does not exist: {root}", file=sys.stderr)
        return 1

    cli = find_cli(args.cli)
    IMAGES.mkdir(parents=True, exist_ok=True)

    if not (IMAGES / f"{DEFAULT_STEM}.png").is_file() or not (IMAGES / f"{DEFAULT_STEM}.vtex").is_file():
        print(f"{DEFAULT_STEM}.png / {DEFAULT_STEM}.vtex are required under {IMAGES}", file=sys.stderr)
        return 1

    # Keep the committed default thumb at the HUD tile size.
    resize_to_tile(IMAGES / f"{DEFAULT_STEM}.png")

    written: dict[str, Path] = {}
    for vpk in discover_vpks(root):
        try:
            process_vpk(cli, vpk, written)
        except Exception as ex:
            print(f"[images] ERROR {vpk}: {ex}", file=sys.stderr)

    stems = [DEFAULT_STEM] + sorted(s for s in written if s != DEFAULT_STEM)
    # Keep previously extracted maps still on disk
    for png in sorted(IMAGES.glob("*.png")):
        stem = png.stem
        if stem not in stems and (IMAGES / f"{stem}.vtex").is_file():
            stems.append(stem)
    stems = [DEFAULT_STEM] + sorted({s for s in stems if s != DEFAULT_STEM})
    rewrite_css(stems)
    print(
        f"[images] updated {len(stems)} CSS classes "
        f"({TARGET_WIDTH}x{TARGET_HEIGHT}) in {HUD_VCSS.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
