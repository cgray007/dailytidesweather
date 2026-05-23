#!/usr/bin/env python3
"""
Music Tagger: Standardize audio file tags and filenames.

Derives artist/album/track info from folder structure and filenames, updates
metadata tags, and renames files to:
  {Artist Name} - {Album Name} - {Track Number} - {Track Name}.{ext}

Folder structure assumed:
  /Music/{Artist}/{Album}/{files}   -> artist + album from folders
  /Music/{Artist}/{files}           -> artist from parent, album from tags
  /Music/{files}                    -> artist + album from tags or filename

Dry-run by default. Pass --apply to write changes.
"""

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Optional

SUPPORTED_EXTENSIONS = {".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".wav", ".aiff"}

# Filename patterns to extract track number and title
FILENAME_PATTERNS = [
    # "01 - Track Title" or "01. Track Title" or "01  Track Title"
    re.compile(r"^(\d+)\s*[-\.]\s*(.+)$"),
    # "Track Title" (no track number)
    re.compile(r"^(.+)$"),
]

TRACK_NUM_PATTERN = re.compile(r"^(\d+)\s*[-\.]\s*(.+)$")


def parse_filename(stem: str) -> tuple[Optional[str], Optional[str]]:
    """Return (track_number, track_title) parsed from a filename stem."""
    stem = stem.strip()
    m = TRACK_NUM_PATTERN.match(stem)
    if m:
        num = m.group(1).lstrip("0") or "0"
        title = m.group(2).strip()
        return num, title
    return None, stem


def safe_filename(name: str) -> str:
    """Strip characters that are unsafe in filenames."""
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip()


def infer_context_from_path(file_path: Path, music_root: Path) -> tuple[Optional[str], Optional[str]]:
    """Return (artist, album) inferred from the folder structure relative to music_root."""
    try:
        rel = file_path.relative_to(music_root)
    except ValueError:
        return None, None

    parts = rel.parts  # e.g. ("Artist", "Album", "track.mp3") or ("Artist", "track.mp3")
    if len(parts) >= 3:
        return parts[0], parts[1]  # Artist/Album/track
    if len(parts) == 2:
        return parts[0], None      # Artist/track — no album subfolder
    return None, None              # file directly in music root


def read_tags(file_path: Path) -> dict:
    """Read metadata tags from an audio file. Returns a dict with normalized keys."""
    try:
        from mutagen import File as MutagenFile
        from mutagen.easyid3 import EasyID3
        from mutagen.mp3 import MP3
        from mutagen.flac import FLAC
        from mutagen.mp4 import MP4
    except ImportError:
        print("ERROR: mutagen is required. Install with: pip install mutagen", file=sys.stderr)
        sys.exit(1)

    ext = file_path.suffix.lower()
    tags: dict = {}

    try:
        if ext == ".mp3":
            try:
                audio = EasyID3(str(file_path))
            except Exception:
                return tags
            tags["artist"] = audio.get("artist", [None])[0]
            tags["album"] = audio.get("album", [None])[0]
            tags["title"] = audio.get("title", [None])[0]
            raw_num = audio.get("tracknumber", [None])[0]
            if raw_num:
                tags["tracknumber"] = raw_num.split("/")[0].lstrip("0") or "0"

        elif ext == ".flac":
            audio = FLAC(str(file_path))
            tags["artist"] = (audio.get("artist") or [None])[0]
            tags["album"] = (audio.get("album") or [None])[0]
            tags["title"] = (audio.get("title") or [None])[0]
            raw_num = (audio.get("tracknumber") or [None])[0]
            if raw_num:
                tags["tracknumber"] = str(raw_num).split("/")[0].lstrip("0") or "0"

        elif ext in (".m4a", ".aac", ".mp4"):
            audio = MP4(str(file_path))
            artist_tag = audio.tags.get("\xa9ART") if audio.tags else None
            album_tag = audio.tags.get("\xa9alb") if audio.tags else None
            title_tag = audio.tags.get("\xa9nam") if audio.tags else None
            trkn_tag = audio.tags.get("trkn") if audio.tags else None
            tags["artist"] = artist_tag[0] if artist_tag else None
            tags["album"] = album_tag[0] if album_tag else None
            tags["title"] = title_tag[0] if title_tag else None
            if trkn_tag:
                tags["tracknumber"] = str(trkn_tag[0][0])

        else:
            audio = MutagenFile(str(file_path), easy=True)
            if audio and audio.tags:
                tags["artist"] = audio.tags.get("artist", [None])[0] if hasattr(audio.tags, "get") else None
                tags["album"] = audio.tags.get("album", [None])[0] if hasattr(audio.tags, "get") else None
                tags["title"] = audio.tags.get("title", [None])[0] if hasattr(audio.tags, "get") else None
                raw_num = audio.tags.get("tracknumber", [None])[0] if hasattr(audio.tags, "get") else None
                if raw_num:
                    tags["tracknumber"] = str(raw_num).split("/")[0].lstrip("0") or "0"

    except Exception as e:
        print(f"  WARNING: Could not read tags from {file_path.name}: {e}", file=sys.stderr)

    return tags


def write_tags(file_path: Path, artist: str, album: str, title: str, tracknumber: str) -> bool:
    """Write standardized tags to the file. Returns True on success."""
    try:
        from mutagen.easyid3 import EasyID3
        from mutagen.mp3 import MP3
        from mutagen.flac import FLAC
        from mutagen.mp4 import MP4
        from mutagen import File as MutagenFile
    except ImportError:
        print("ERROR: mutagen is required. Install with: pip install mutagen", file=sys.stderr)
        sys.exit(1)

    ext = file_path.suffix.lower()

    try:
        if ext == ".mp3":
            try:
                audio = EasyID3(str(file_path))
            except Exception:
                from mutagen.id3 import ID3NoHeaderError
                audio = EasyID3()
                audio.save(str(file_path))
                audio = EasyID3(str(file_path))
            audio["artist"] = artist
            audio["album"] = album
            audio["title"] = title
            audio["tracknumber"] = tracknumber
            audio.save()

        elif ext == ".flac":
            audio = FLAC(str(file_path))
            audio["artist"] = artist
            audio["album"] = album
            audio["title"] = title
            audio["tracknumber"] = tracknumber
            audio.save()

        elif ext in (".m4a", ".aac", ".mp4"):
            audio = MP4(str(file_path))
            if audio.tags is None:
                audio.add_tags()
            audio.tags["\xa9ART"] = [artist]
            audio.tags["\xa9alb"] = [album]
            audio.tags["\xa9nam"] = [title]
            track_int = int(tracknumber) if tracknumber.isdigit() else 0
            audio.tags["trkn"] = [(track_int, 0)]
            audio.save()

        else:
            audio = MutagenFile(str(file_path), easy=True)
            if audio is None:
                return False
            if audio.tags is None:
                audio.add_tags()
            audio.tags["artist"] = [artist]
            audio.tags["album"] = [album]
            audio.tags["title"] = [title]
            audio.tags["tracknumber"] = [tracknumber]
            audio.save()

        return True

    except Exception as e:
        print(f"  ERROR writing tags to {file_path.name}: {e}", file=sys.stderr)
        return False


def build_new_filename(artist: str, album: str, tracknumber: str, title: str, ext: str) -> str:
    """Build the standard filename: {Artist} - {Album} - {TrackNum:02d} - {Title}.{ext}"""
    try:
        num = int(tracknumber)
        track_str = f"{num:02d}"
    except (ValueError, TypeError):
        track_str = "00"

    name = f"{safe_filename(artist)} - {safe_filename(album)} - {track_str} - {safe_filename(title)}{ext}"
    return name


def process_file(
    file_path: Path,
    music_root: Path,
    apply: bool,
    verbose: bool,
) -> dict:
    """Process a single audio file. Returns a result dict."""
    result = {
        "path": str(file_path),
        "skipped": False,
        "skip_reason": None,
        "new_path": None,
        "tags_updated": False,
        "renamed": False,
    }

    # Read existing tags
    tags = read_tags(file_path)

    # Determine artist and album from folder structure, fall back to tags
    folder_artist, folder_album = infer_context_from_path(file_path, music_root)
    artist = tags.get("artist") or folder_artist
    album = tags.get("album") or folder_album

    if not artist:
        # Last resort: split filename on " - " (e.g. "Artist - Title.mp3")
        stem = file_path.stem
        parts = stem.split(" - ", 1)
        if len(parts) == 2:
            artist = parts[0].strip()

    if not artist:
        result["skipped"] = True
        result["skip_reason"] = "Could not determine artist"
        return result

    if not album:
        album = "Unknown Album"

    # Determine track number and title
    tracknumber = tags.get("tracknumber")
    title = tags.get("title")

    if not tracknumber or not title:
        parsed_num, parsed_title = parse_filename(file_path.stem)
        if not tracknumber:
            tracknumber = parsed_num or "0"
        if not title:
            title = parsed_title

    if not title:
        title = file_path.stem

    tracknumber = str(tracknumber) if tracknumber else "0"

    # Build target filename
    new_name = build_new_filename(artist, album, tracknumber, title, file_path.suffix.lower())
    new_path = file_path.parent / new_name
    result["new_path"] = str(new_path)

    needs_rename = new_path.name != file_path.name
    needs_tag_update = (
        tags.get("artist") != artist
        or tags.get("album") != album
        or tags.get("title") != title
        or tags.get("tracknumber") != tracknumber
    )

    if verbose or not apply:
        print(f"\n  File    : {file_path.name}")
        print(f"  Artist  : {artist}")
        print(f"  Album   : {album}")
        print(f"  Track # : {tracknumber}")
        print(f"  Title   : {title}")
        if needs_rename:
            print(f"  Rename  : {new_name}")
        else:
            print(f"  Rename  : (no change)")
        if needs_tag_update:
            print(f"  Tags    : will update")
        else:
            print(f"  Tags    : (no change)")

    if apply:
        if needs_tag_update:
            ok = write_tags(file_path, artist, album, title, tracknumber)
            result["tags_updated"] = ok

        if needs_rename:
            try:
                # Avoid overwriting an existing different file
                if new_path.exists() and new_path != file_path:
                    print(f"  WARNING: target already exists, skipping rename: {new_name}", file=sys.stderr)
                else:
                    file_path.rename(new_path)
                    result["renamed"] = True
            except Exception as e:
                print(f"  ERROR renaming {file_path.name}: {e}", file=sys.stderr)

    return result


def scan_music(music_root: Path, apply: bool, verbose: bool, extensions: set) -> None:
    music_root = music_root.resolve()

    if not music_root.exists():
        print(f"ERROR: Music folder not found: {music_root}", file=sys.stderr)
        sys.exit(1)

    print(f"{'APPLYING changes' if apply else 'DRY RUN (pass --apply to write changes)'}")
    print(f"Scanning: {music_root}\n")

    total = skipped = updated_tags = renamed = 0

    for root, dirs, files in os.walk(music_root):
        dirs.sort()
        for fname in sorted(files):
            file_path = Path(root) / fname
            if file_path.suffix.lower() not in extensions:
                continue

            total += 1
            if not apply:
                rel = file_path.relative_to(music_root)
                print(f"[{total}] {rel}")

            result = process_file(file_path, music_root, apply=apply, verbose=verbose)

            if result["skipped"]:
                skipped += 1
                print(f"  SKIP: {result['skip_reason']}")
            else:
                if result["tags_updated"]:
                    updated_tags += 1
                if result["renamed"]:
                    renamed += 1

    print(f"\n{'='*60}")
    print(f"Total files   : {total}")
    print(f"Skipped       : {skipped}")
    if apply:
        print(f"Tags updated  : {updated_tags}")
        print(f"Files renamed : {renamed}")
    else:
        print(f"(Run with --apply to commit these changes)")


def main():
    parser = argparse.ArgumentParser(
        description="Standardize music file tags and filenames.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "music_dir",
        nargs="?",
        default="/Music",
        help="Root music directory (default: /Music)",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Write tag changes and rename files (default is dry-run)",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Show details for every file even when applying",
    )
    parser.add_argument(
        "--ext",
        nargs="+",
        default=None,
        metavar="EXT",
        help="Limit to specific extensions, e.g. --ext .mp3 .flac",
    )
    args = parser.parse_args()

    extensions = {e.lower() if e.startswith(".") else f".{e.lower()}" for e in args.ext} if args.ext else SUPPORTED_EXTENSIONS

    scan_music(
        music_root=Path(args.music_dir),
        apply=args.apply,
        verbose=args.verbose,
        extensions=extensions,
    )


if __name__ == "__main__":
    main()
