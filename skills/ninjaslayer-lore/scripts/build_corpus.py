#!/usr/bin/env python3
"""Build a deterministic, private searchable corpus from the configured EPUBs."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import html
from html.parser import HTMLParser
import json
import os
from pathlib import Path, PurePosixPath
import posixpath
import re
import shutil
import sys
import tempfile
import unicodedata
from urllib.parse import unquote
import xml.etree.ElementTree as ET
import zipfile


EXTRACTOR_VERSION = 2
MIN_CHUNK_CHARS = 8_000
MAX_CHUNK_CHARS = 12_000
BLOCK_TAGS = {"p", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "pre", "dt", "dd"}
SKIP_TAGS = {"script", "style", "svg", "math", "noscript"}


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def normalize_text(value: str) -> str:
    value = unicodedata.normalize("NFC", html.unescape(value))
    value = value.replace("\u00a0", " ").replace("\u3000", " ")
    return re.sub(r"\s+", " ", value).strip()


def normalize_href(base_entry: str, href: str) -> tuple[str, str]:
    href = unquote(href).replace("\\", "/")
    path, _, fragment = href.partition("#")
    joined = posixpath.normpath(posixpath.join(posixpath.dirname(base_entry), path))
    return joined.lstrip("./"), unquote(fragment)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n", delete=False, dir=path.parent) as stream:
        stream.write(text)
        temporary = Path(stream.name)
    os.replace(temporary, path)


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def safe_remove_tree(path: Path, repo_root: Path) -> None:
    expected_parent = (repo_root / ".lore-cache" / "corpus").resolve()
    resolved = path.resolve()
    if resolved.parent != expected_parent or not resolved.name.startswith("part"):
        raise RuntimeError(f"Refusing to remove unexpected path: {resolved}")
    if resolved.exists():
        shutil.rmtree(resolved)


class ParagraphParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.stack: list[str] = []
        self.skip_depth = 0
        self.active_tag: str | None = None
        self.active_depth = 0
        self.active_text: list[str] = []
        self.paragraphs: list[str] = []
        self.anchor_positions: dict[str, int] = {}
        self.fallback_text: list[str] = []

    def handle_starttag(self, tag: str, attrs) -> None:
        tag = tag.lower()
        self.stack.append(tag)
        attr_map = {str(key).lower(): value for key, value in attrs if value is not None}
        for key in ("id", "name"):
            if attr_map.get(key):
                self.anchor_positions.setdefault(unquote(attr_map[key]), len(self.paragraphs))
        if tag in SKIP_TAGS:
            self.skip_depth += 1
            return
        if self.skip_depth:
            return
        if tag == "br" and self.active_tag is not None:
            self.active_text.append(" ")
        if self.active_tag is None and tag in BLOCK_TAGS:
            self.active_tag = tag
            self.active_depth = len(self.stack)
            self.active_text = []

    def handle_startendtag(self, tag: str, attrs) -> None:
        attr_map = {str(key).lower(): value for key, value in attrs if value is not None}
        for key in ("id", "name"):
            if attr_map.get(key):
                self.anchor_positions.setdefault(unquote(attr_map[key]), len(self.paragraphs))

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()
        if not self.stack:
            return
        if self.active_tag == tag and len(self.stack) == self.active_depth:
            text = normalize_text("".join(self.active_text))
            if text:
                self.paragraphs.append(text)
            self.active_tag = None
            self.active_depth = 0
            self.active_text = []
        if tag in SKIP_TAGS and self.skip_depth:
            self.skip_depth -= 1
        while self.stack:
            popped = self.stack.pop()
            if popped == tag:
                break

    def handle_data(self, data: str) -> None:
        if self.skip_depth:
            return
        self.fallback_text.append(data)
        if self.active_tag is not None:
            self.active_text.append(data)

    def finish(self) -> tuple[list[str], dict[str, int]]:
        if self.active_tag is not None:
            text = normalize_text("".join(self.active_text))
            if text:
                self.paragraphs.append(text)
        if not self.paragraphs:
            fallback = normalize_text("".join(self.fallback_text))
            if fallback:
                self.paragraphs = [fallback]
        return self.paragraphs, self.anchor_positions


def decode_markup(raw: bytes) -> str:
    encoding_match = re.search(br"encoding=[\"']([^\"']+)", raw[:200], re.IGNORECASE)
    encodings = [encoding_match.group(1).decode("ascii", "ignore")] if encoding_match else []
    encodings.extend(["utf-8-sig", "utf-16", "gb18030"])
    for encoding in encodings:
        if not encoding:
            continue
        try:
            return raw.decode(encoding)
        except (LookupError, UnicodeDecodeError):
            pass
    return raw.decode("utf-8", "replace")


def parse_paragraphs(raw: bytes) -> tuple[list[str], dict[str, int]]:
    parser = ParagraphParser()
    parser.feed(decode_markup(raw))
    parser.close()
    return parser.finish()


def find_rootfile(zip_file: zipfile.ZipFile) -> str:
    root = ET.fromstring(zip_file.read("META-INF/container.xml"))
    for element in root.iter():
        if local_name(element.tag) == "rootfile" and element.attrib.get("full-path"):
            return unquote(element.attrib["full-path"]).replace("\\", "/")
    raise RuntimeError("EPUB container.xml does not contain a rootfile")


def parse_opf(zip_file: zipfile.ZipFile, opf_entry: str):
    root = ET.fromstring(zip_file.read(opf_entry))
    manifest: dict[str, dict[str, str]] = {}
    spine_ids: list[str] = []
    spine_toc = ""
    title = ""
    for element in root.iter():
        name = local_name(element.tag)
        if name == "item" and element.attrib.get("id") and element.attrib.get("href"):
            entry, _ = normalize_href(opf_entry, element.attrib["href"])
            manifest[element.attrib["id"]] = {
                "entry": entry,
                "media_type": element.attrib.get("media-type", ""),
            }
        elif name == "spine":
            spine_toc = element.attrib.get("toc", "")
        elif name == "itemref" and element.attrib.get("idref"):
            spine_ids.append(element.attrib["idref"])
        elif name == "title" and not title:
            title = normalize_text("".join(element.itertext()))
    spine = [manifest[item_id]["entry"] for item_id in spine_ids if item_id in manifest]
    ncx_entry = ""
    if spine_toc in manifest:
        ncx_entry = manifest[spine_toc]["entry"]
    if not ncx_entry:
        for item in manifest.values():
            if item["media_type"] == "application/x-dtbncx+xml" or item["entry"].lower().endswith(".ncx"):
                ncx_entry = item["entry"]
                break
    return title, manifest, spine, ncx_entry


def first_descendant(element: ET.Element, wanted: str) -> ET.Element | None:
    for child in element.iter():
        if local_name(child.tag) == wanted:
            return child
    return None


def parse_ncx(zip_file: zipfile.ZipFile, ncx_entry: str) -> list[dict]:
    if not ncx_entry:
        return []
    root = ET.fromstring(zip_file.read(ncx_entry))
    navpoints: list[dict] = []
    for element in root.iter():
        if local_name(element.tag) != "navPoint":
            continue
        label_element = first_descendant(element, "navLabel")
        text_element = first_descendant(label_element, "text") if label_element is not None else None
        content_element = first_descendant(element, "content")
        if content_element is None or not content_element.attrib.get("src"):
            continue
        entry, fragment = normalize_href(ncx_entry, content_element.attrib["src"])
        label = normalize_text("".join(text_element.itertext())) if text_element is not None else ""
        navpoints.append({
            "order": len(navpoints) + 1,
            "number": len(navpoints) + 1,
            "label": label or f"章节 {len(navpoints) + 1}",
            "entry": entry,
            "fragment": fragment,
        })
    return navpoints


def resolve_zip_name(zip_file: zipfile.ZipFile, entry: str) -> str | None:
    names = getattr(zip_file, "_lore_names", None)
    if names is None:
        names = {name.casefold(): name for name in zip_file.namelist()}
        setattr(zip_file, "_lore_names", names)
    return names.get(entry.casefold())


def should_skip_page(entry: str, paragraphs: list[str]) -> bool:
    total = sum(len(item) for item in paragraphs)
    stem = PurePosixPath(entry).stem.casefold()
    return total < 300 and any(marker in stem for marker in ("cover", "titlepage", "copyright", "toc"))


def chapter_key(chapter: dict) -> str:
    return f"{chapter['number']:03d}:{chapter['label']}"


def extract_book(source: dict, source_path: Path, repo_root: Path, digest: str) -> dict:
    corpus_dir = repo_root / ".lore-cache" / "corpus" / source["id"]
    safe_remove_tree(corpus_dir, repo_root)
    corpus_dir.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(source_path) as archive:
        opf_entry = find_rootfile(archive)
        embedded_title, manifest, spine, ncx_entry = parse_opf(archive, opf_entry)
        navpoints = parse_ncx(archive, ncx_entry)
        nav_by_entry: dict[str, list[dict]] = {}
        for nav in navpoints:
            nav_by_entry.setdefault(nav["entry"].casefold(), []).append(nav)

        chapters: dict[str, dict] = {}
        current: dict | None = None
        fallback_number = len(navpoints)

        for spine_index, entry in enumerate(spine, start=1):
            actual_name = resolve_zip_name(archive, entry)
            if actual_name is None:
                raise RuntimeError(f"Spine entry not found in {source_path.name}: {entry}")
            paragraphs, anchors = parse_paragraphs(archive.read(actual_name))
            starts: dict[int, list[dict]] = {}
            for nav in nav_by_entry.get(entry.casefold(), []):
                position = anchors.get(nav["fragment"], 0) if nav["fragment"] else 0
                position = min(max(position, 0), len(paragraphs))
                starts.setdefault(position, []).append(nav)

            if not navpoints:
                fallback_number += 1
                current = {
                    "number": fallback_number,
                    "label": PurePosixPath(entry).stem,
                    "source": "spine-fallback",
                }
                starts.setdefault(0, []).append(current)

            skip_page = should_skip_page(entry, paragraphs)
            for position in range(len(paragraphs) + 1):
                if position in starts:
                    current = starts[position][-1]
                if position == len(paragraphs) or skip_page:
                    continue
                if current is None:
                    fallback_number += 1
                    current = {
                        "number": fallback_number,
                        "label": PurePosixPath(entry).stem,
                        "source": "pre-navigation-fallback",
                    }
                key = chapter_key(current)
                chapter = chapters.setdefault(key, {
                    "number": current["number"],
                    "label": current["label"],
                    "navigation_source": "ncx" if "order" in current else current["source"],
                    "paragraphs": [],
                    "char_count": 0,
                })
                text = paragraphs[position]
                char_start = chapter["char_count"]
                char_end = char_start + len(text)
                chapter["paragraphs"].append({
                    "text": text,
                    "entry": entry,
                    "spine_index": spine_index,
                    "char_start": char_start,
                    "char_end": char_end,
                })
                chapter["char_count"] = char_end + 1

    ordered_chapters = sorted(chapters.values(), key=lambda item: item["number"])
    chunks: list[dict] = []
    total_chars = 0
    for chapter_index, chapter in enumerate(ordered_chapters, start=1):
        chapter["number"] = chapter_index
        paragraph_groups: list[list[dict]] = []
        current_group: list[dict] = []
        current_chars = 0
        for paragraph in chapter.pop("paragraphs"):
            added = len(paragraph["text"]) + (1 if current_group else 0)
            if current_group and current_chars >= MIN_CHUNK_CHARS and current_chars + added > MAX_CHUNK_CHARS:
                paragraph_groups.append(current_group)
                current_group = []
                current_chars = 0
                added = len(paragraph["text"])
            current_group.append(paragraph)
            current_chars += added
        if current_group:
            paragraph_groups.append(current_group)

        chapter_chunks: list[str] = []
        for segment_index, group in enumerate(paragraph_groups, start=1):
            filename = f"{source['prefix']}-c{chapter_index:03d}-s{segment_index:02d}.md"
            source_entries = list(dict.fromkeys(item["entry"] for item in group))
            metadata = {
                "schema": 1,
                "book_id": source["id"],
                "part": source["part"],
                "book_title": source["title"],
                "chapter_number": chapter_index,
                "chapter": chapter["label"],
                "chunk": filename,
                "source_epub": source["filename"],
                "source_sha256": digest,
                "source_entries": source_entries,
                "spine_start": min(item["spine_index"] for item in group),
                "spine_end": max(item["spine_index"] for item in group),
                "char_start": group[0]["char_start"],
                "char_end": group[-1]["char_end"],
            }
            body = "\n".join(item["text"] for item in group)
            text = (
                "<!-- lore-meta: "
                + json.dumps(metadata, ensure_ascii=False, separators=(",", ":"))
                + " -->\n"
                + f"# {source['part']}｜{chapter['label']}\n\n"
                + body
                + "\n"
            )
            atomic_write_text(corpus_dir / filename, text)
            chunk_record = {
                "file": filename,
                "chapter_number": chapter_index,
                "chapter": chapter["label"],
                "char_start": metadata["char_start"],
                "char_end": metadata["char_end"],
                "text_chars": sum(len(item["text"]) for item in group),
                "paragraphs": len(group),
                "source_entries": source_entries,
                "spine_start": metadata["spine_start"],
                "spine_end": metadata["spine_end"],
            }
            chunks.append(chunk_record)
            chapter_chunks.append(filename)
            total_chars += chunk_record["text_chars"]
        chapter["chunks"] = chapter_chunks
        chapter["char_count"] = max(chapter["char_count"] - 1, 0)

    if not chunks or any(chunk["text_chars"] == 0 for chunk in chunks):
        raise RuntimeError(f"Extraction produced an empty corpus for {source_path.name}")

    stat = source_path.stat()
    duplicate_spine_entries = sorted(entry for entry, count in Counter(spine).items() if count > 1)
    return {
        "id": source["id"],
        "prefix": source["prefix"],
        "part": source["part"],
        "title": source["title"],
        "embedded_title": embedded_title,
        "filename": source["filename"],
        "sha256": digest,
        "size_bytes": stat.st_size,
        "mtime_ns": stat.st_mtime_ns,
        "opf_entry": opf_entry,
        "ncx_entry": ncx_entry,
        "manifest_items": len(manifest),
        "spine_items": len(spine),
        "spine": [{"index": index, "entry": entry} for index, entry in enumerate(spine, start=1)],
        "duplicate_spine_entries": duplicate_spine_entries,
        "navigation_items": len(navpoints),
        "navigation": navpoints,
        "chapter_count": len(ordered_chapters),
        "chunk_count": len(chunks),
        "text_chars": total_chars,
        "chapters": ordered_chapters,
        "chunks": chunks,
    }


def render_library_index(books: list[dict]) -> str:
    lines = [
        "# Ninja Slayer 原著书目与章节索引",
        "",
        "> 本文件由 `skills/ninjaslayer-lore/scripts/build_corpus.py` 生成。只记录结构、哈希与私有缓存文件名，不包含原文。",
        "",
        "## 书目概览",
        "",
        "| 书部 | EPUB 文件 | SHA-256 | Spine | NCX 导航 | 逻辑章节 | 检索块 | 正文字符 |",
        "|---|---|---|---:|---:|---:|---:|---:|",
    ]
    for book in books:
        lines.append(
            f"| {book['part']} | `{book['filename']}` | `{book['sha256']}` | {book['spine_items']} | "
            f"{book['navigation_items']} | {book['chapter_count']} | {book['chunk_count']} | {book['text_chars']:,} |"
        )
    lines.extend(["", "## 章节与检索块", ""])
    for book in books:
        lines.extend([
            f"### {book['part']}：{book['title']}",
            "",
            f"- OPF：`{book['opf_entry']}`",
            f"- NCX：`{book['ncx_entry'] or '无（按 spine 回退）'}`",
            "",
            "| # | 章节 | 字符 | 私有缓存块 |",
            "|---:|---|---:|---|",
        ])
        for chapter in book["chapters"]:
            files = "、".join(f"`{name}`" for name in chapter["chunks"])
            label = chapter["label"].replace("|", "\\|")
            lines.append(f"| {chapter['number']} | {label} | {chapter['char_count']:,} | {files} |")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", required=True, type=Path)
    parser.add_argument("--source-root", type=Path)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    repo_root = args.repo_root.expanduser().resolve()
    sources_file = repo_root / "Docs" / "lore" / "sources.json"
    if not sources_file.is_file():
        parser.error(f"Missing source manifest: {sources_file}")
    source_root = (args.source_root or (Path.home() / "Documents" / "忍者杀手" / "LocalBooks")).expanduser().resolve()
    sources = load_json(sources_file)
    source_manifest_digest = sha256_file(sources_file)
    paths = {source["id"]: source_root / source["filename"] for source in sources}
    missing = [str(path) for path in paths.values() if not path.is_file()]
    if missing:
        parser.error("Missing EPUB source(s):\n  " + "\n  ".join(missing))

    cache_root = repo_root / ".lore-cache"
    manifest_path = cache_root / "manifest.json"
    old_manifest = load_json(manifest_path) if manifest_path.is_file() else {}
    old_books = {book["id"]: book for book in old_manifest.get("books", [])}
    compatible = (
        old_manifest.get("extractor_version") == EXTRACTOR_VERSION
        and old_manifest.get("source_manifest_sha256") == source_manifest_digest
    )
    books: list[dict] = []

    for source in sources:
        path = paths[source["id"]]
        digest = sha256_file(path)
        old_book = old_books.get(source["id"])
        chunk_dir = cache_root / "corpus" / source["id"]
        expected_chunks_exist = bool(old_book) and all((chunk_dir / item["file"]).is_file() for item in old_book.get("chunks", []))
        if not args.force and compatible and old_book and old_book.get("sha256") == digest and expected_chunks_exist:
            print(f"[unchanged] {source['part']} {source['filename']}")
            books.append(old_book)
            continue
        print(f"[extract] {source['part']} {source['filename']}")
        books.append(extract_book(source, path, repo_root, digest))

    manifest = {
        "schema": 1,
        "extractor_version": EXTRACTOR_VERSION,
        "source_manifest_sha256": source_manifest_digest,
        "source_root": str(source_root),
        "chunk_chars": {"minimum": MIN_CHUNK_CHARS, "maximum": MAX_CHUNK_CHARS},
        "books": books,
        "total_text_chars": sum(book["text_chars"] for book in books),
        "total_chunks": sum(book["chunk_count"] for book in books),
    }
    atomic_write_text(manifest_path, json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    atomic_write_text(repo_root / "Docs" / "lore" / "library-index.md", render_library_index(books))
    print(f"[done] {manifest['total_chunks']} chunks, {manifest['total_text_chars']:,} text characters")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ET.ParseError, zipfile.BadZipFile, RuntimeError, KeyError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
