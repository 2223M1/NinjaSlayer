#!/usr/bin/env python3
"""Sync the local tutorials.sts2modding.com mirror from the live site.

The live site is the source of truth. This script discovers every docs page
linked from the home page, downloads each page, converts the article body to
plain Markdown, and writes:

  Docs/tutorials.sts2modding.com/docs/.../index.md
  Docs/tutorials.sts2modding.com/_sync_manifest.json
  Docs/STS2_Modding_Reference.md

It intentionally uses only Python's standard library so future syncs do not
depend on local package setup.
"""

from __future__ import annotations

import argparse
import datetime as dt
import html
import json
import posixpath
import re
import shutil
import sys
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterable


SOURCE_ROOT = "https://tutorials.sts2modding.com/"
SOURCE_REPOSITORY = "https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials"
MIRROR_DIR = Path("Docs/tutorials.sts2modding.com")
DOCS_DIR = MIRROR_DIR / "docs"
MANIFEST_PATH = MIRROR_DIR / "_sync_manifest.json"
REFERENCE_PATH = Path("Docs/STS2_Modding_Reference.md")


@dataclass(frozen=True)
class Page:
    url: str
    route: str
    title: str
    path: str


class LinkCollector(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.links: list[tuple[str, str]] = []
        self._href_stack: list[str | None] = []
        self._text_parts: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag != "a":
            return
        href = dict(attrs).get("href")
        self._href_stack.append(href)
        self._text_parts = []

    def handle_endtag(self, tag: str) -> None:
        if tag != "a" or not self._href_stack:
            return
        href = self._href_stack.pop()
        text = normalize_ws("".join(self._text_parts))
        if href:
            self.links.append((href, text))
        self._text_parts = []

    def handle_data(self, data: str) -> None:
        if self._href_stack:
            self._text_parts.append(data)


class TitleParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.title = ""
        self._in_h1 = False
        self._parts: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "h1" and not self.title:
            self._in_h1 = True
            self._parts = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "h1" and self._in_h1:
            self.title = normalize_ws("".join(self._parts))
            self._in_h1 = False

    def handle_data(self, data: str) -> None:
        if self._in_h1:
            self._parts.append(data)


class ArticleMarkdownParser(HTMLParser):
    block_tags = {
        "p",
        "div",
        "section",
        "article",
        "blockquote",
        "ul",
        "ol",
        "li",
        "table",
        "thead",
        "tbody",
        "tr",
        "td",
        "th",
    }

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
        self.stack: list[tuple[str, dict[str, str]]] = []
        self.skip_depth = 0
        self.in_article = False
        self.in_code_figure = False
        self.in_code_cell = False
        self.in_gutter = False
        self.code_lang = ""
        self.code_parts: list[str] = []
        self.inline_code_depth = 0
        self.heading: int | None = None
        self.list_stack: list[str] = []

    def handle_starttag(self, tag: str, attrs_raw: list[tuple[str, str | None]]) -> None:
        attrs = {k: v or "" for k, v in attrs_raw}
        classes = set(attrs.get("class", "").split())
        self.stack.append((tag, attrs))

        if tag == "article":
            self.in_article = True
            return

        if not self.in_article:
            return

        if self.skip_depth:
            self.skip_depth += 1
            return

        if "kira-post-title" in classes or "kira-post-meta" in classes:
            self.skip_depth = 1
            return

        if tag == "figure" and "highlight" in classes:
            self.in_code_figure = True
            self.code_lang = next((c for c in classes if c != "highlight"), "")
            self.code_parts = []
            return

        if self.in_code_figure:
            if tag == "td" and "gutter" in classes:
                self.in_gutter = True
            elif tag == "td" and "code" in classes:
                self.in_code_cell = True
            elif tag == "br" and self.in_code_cell and not self.in_gutter:
                self.code_parts.append("\n")
            return

        if tag in ("h1", "h2", "h3", "h4", "h5", "h6"):
            self.heading = int(tag[1])
            self._blank()
            self.parts.append("#" * self.heading + " ")
        elif tag == "blockquote":
            self._blank()
            self.parts.append("> ")
        elif tag in ("ul", "ol"):
            self.list_stack.append(tag)
            self._newline()
        elif tag == "li":
            self._newline()
            self.parts.append("- ")
        elif tag == "br":
            self._newline()
        elif tag == "code":
            self.inline_code_depth += 1
            self.parts.append("`")
        elif tag == "pre":
            self._blank()
            self.parts.append("```\n")
        elif tag == "a":
            href = attrs.get("href", "")
            if href:
                self.parts.append("[")
        elif tag in self.block_tags:
            self._newline()

    def handle_endtag(self, tag: str) -> None:
        if not self.in_article:
            if self.stack:
                self.stack.pop()
            return

        if self.skip_depth:
            self.skip_depth -= 1
            if self.stack:
                self.stack.pop()
            return

        if self.in_code_figure:
            if tag == "td" and self.in_gutter:
                self.in_gutter = False
            elif tag == "td" and self.in_code_cell:
                self.in_code_cell = False
            elif tag == "figure":
                code = html.unescape("".join(self.code_parts)).strip("\n")
                self._blank()
                self.parts.append(f"```{self.code_lang}\n{code}\n```\n")
                self.in_code_figure = False
                self.code_lang = ""
                self.code_parts = []
            if self.stack:
                self.stack.pop()
            return

        if tag in ("h1", "h2", "h3", "h4", "h5", "h6"):
            self.heading = None
            self._blank()
        elif tag == "blockquote":
            self._blank()
        elif tag in ("ul", "ol") and self.list_stack:
            self.list_stack.pop()
            self._blank()
        elif tag == "li":
            self._newline()
        elif tag == "code" and self.inline_code_depth:
            self.inline_code_depth -= 1
            self.parts.append("`")
        elif tag == "pre":
            self.parts.append("\n```\n")
        elif tag == "a":
            attrs = self.stack[-1][1] if self.stack else {}
            href = attrs.get("href", "")
            if href:
                self.parts.append(f"]({href})")
        elif tag in self.block_tags:
            self._newline()
        elif tag == "article":
            self.in_article = False

        if self.stack:
            self.stack.pop()

    def handle_data(self, data: str) -> None:
        if not self.in_article or self.skip_depth:
            return

        if self.in_code_figure:
            if self.in_code_cell and not self.in_gutter:
                self.code_parts.append(data)
            return

        text = html.unescape(data)
        if self.inline_code_depth:
            self.parts.append(text)
        else:
            text = re.sub(r"\s+", " ", text)
            if text.strip():
                self.parts.append(text)

    def get_markdown(self) -> str:
        markdown = "".join(self.parts)
        markdown = re.sub(r"[ \t]+\n", "\n", markdown)
        markdown = re.sub(r"\n{3,}", "\n\n", markdown)
        markdown = re.sub(r"^\s+", "", markdown)
        return markdown.rstrip() + "\n"

    def _newline(self) -> None:
        if not self.parts:
            return
        if not self.parts[-1].endswith("\n"):
            self.parts.append("\n")

    def _blank(self) -> None:
        if not self.parts:
            return
        current = "".join(self.parts[-3:])
        if current.endswith("\n\n"):
            return
        if current.endswith("\n"):
            self.parts.append("\n")
        else:
            self.parts.append("\n\n")


def normalize_ws(value: str) -> str:
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def fetch_text(url: str, timeout: int = 30, retries: int = 3) -> str:
    request = urllib.request.Request(
        url,
        headers={
            # The site may serve mojibake content to non-browser user agents.
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NinjaSlayer-doc-sync/1.0",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Charset": "utf-8",
        },
    )
    last_error: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read().decode("utf-8", errors="replace")
        except Exception as exc:  # noqa: BLE001 - report the real fetch error.
            last_error = exc
            if attempt + 1 < retries:
                time.sleep(1 + attempt)
    raise RuntimeError(f"failed to fetch {url}: {last_error}")


def discover_pages(home_html: str) -> list[Page]:
    collector = LinkCollector()
    collector.feed(home_html)

    seen: set[str] = set()
    pages: list[Page] = []
    for href, link_text in collector.links:
        absolute = urllib.parse.urljoin(SOURCE_ROOT, href)
        parsed = urllib.parse.urlparse(absolute)
        route = parsed.path.lstrip("/")
        if not route.startswith("docs/"):
            continue
        route = route.rstrip("/") + "/"
        if route in seen:
            continue
        seen.add(route)

        slug = route.removeprefix("docs/").rstrip("/")
        title = link_text or slug.rsplit("/", 1)[-1]
        path = posixpath.join("tutorials.sts2modding.com", route, "index.md")
        pages.append(Page(url=urllib.parse.urljoin(SOURCE_ROOT, route), route=route, title=title, path=path))

    return pages


def page_title(page_html: str, fallback: str) -> str:
    parser = TitleParser()
    parser.feed(page_html)
    return parser.title or fallback


def page_markdown(page: Page, page_html: str) -> str:
    title = page_title(page_html, page.title)
    parser = ArticleMarkdownParser()
    parser.feed(page_html)
    body = parser.get_markdown()
    body = re.sub(rf"^#\s+{re.escape(title)}\s*\n+", "", body)
    return f"# {title}\n\n<!-- Source: {page.url} -->\n\n{body}"


def write_reference(pages: Iterable[Page]) -> None:
    lines = [
        "# STS2 Modding Tutorial Mirror",
        "",
        f"Source: {SOURCE_ROOT}",
        f"Repository: {SOURCE_REPOSITORY}",
        f"Manifest: [tutorials.sts2modding.com/_sync_manifest.json](tutorials.sts2modding.com/_sync_manifest.json)",
        "",
        "Update with:",
        "",
        "```powershell",
        ".\\Docs\\Sync-Sts2Tutorials.ps1",
        "```",
        "",
        "## Pages",
        "",
    ]
    for page in pages:
        lines.append(f"- [{page.title}]({page.path})")
    REFERENCE_PATH.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_manifest(pages: list[Page], failures: list[dict[str, str]]) -> None:
    manifest = {
        "source": SOURCE_ROOT,
        "sourceRepository": SOURCE_REPOSITORY,
        "syncedAt": dt.datetime.now(dt.timezone.utc).astimezone().isoformat(timespec="seconds"),
        "pageCount": len(pages),
        "pages": [page.__dict__ for page in pages],
        "failureCount": len(failures),
        "failures": failures,
    }
    MANIFEST_PATH.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sync(dry_run: bool = False) -> int:
    home_html = fetch_text(SOURCE_ROOT)
    pages = discover_pages(home_html)
    if not pages:
        raise RuntimeError("no docs pages found from site navigation")

    rendered: dict[Page, str] = {}
    failures: list[dict[str, str]] = []
    for page in pages:
        try:
            rendered[page] = page_markdown(page, fetch_text(page.url))
        except Exception as exc:  # noqa: BLE001 - keep syncing other pages.
            failures.append({"url": page.url, "path": page.path, "error": str(exc)})

    if dry_run:
        print(f"Discovered {len(pages)} pages; renderable {len(rendered)}; failures {len(failures)}")
        return 1 if failures else 0

    if DOCS_DIR.exists():
        shutil.rmtree(DOCS_DIR)
    DOCS_DIR.mkdir(parents=True, exist_ok=True)
    MIRROR_DIR.mkdir(parents=True, exist_ok=True)

    for page, markdown in rendered.items():
        output_path = Path("Docs") / page.path
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(markdown, encoding="utf-8", newline="\n")

    write_manifest(pages, failures)
    write_reference(pages)
    print(f"Synced {len(rendered)}/{len(pages)} tutorials to {MIRROR_DIR}")
    if failures:
        print(f"Failures: {len(failures)}", file=sys.stderr)
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="discover and render pages without writing files")
    args = parser.parse_args()
    return sync(dry_run=args.dry_run)


if __name__ == "__main__":
    raise SystemExit(main())
