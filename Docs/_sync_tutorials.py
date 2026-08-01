#!/usr/bin/env python3
"""Sync the local STS2 modding tutorial mirrors.

The legacy tutorial site is mirrored from its rendered HTML because its route
names do not map directly to the source repository. The RitsuLib site is
mirrored from the Markdown files at the exact commit currently at the tip of
its source repository. The script writes:

  Docs/tutorials.sts2modding.com/...
  Docs/sts2-ritsulib.ritsukage.com/...
  Docs/STS2_Modding_Reference.md

All pages for both sites are fetched before any existing mirror is replaced.
Each mirror directory and the combined reference are then replaced through a
same-directory staging path. No third-party Python packages are needed. Curl
is preferred for the repository archive, and Git must pin it to an exact
commit before any archive is downloaded.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import datetime as dt
import html
import io
import json
import os
import posixpath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath
from typing import Callable, Iterable


REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS_ROOT = REPO_ROOT / "Docs"
REFERENCE_PATH = DOCS_ROOT / "STS2_Modding_Reference.md"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NinjaSlayer-doc-sync/2.0"
MAX_FETCH_WORKERS = 8


@dataclass(frozen=True)
class SiteSpec:
    key: str
    label: str
    source_root: str
    source_repository: str
    mirror_dir: Path
    source_mode: str


LEGACY_SITE = SiteSpec(
    key="tutorials.sts2modding.com",
    label="Slay the Spire 2 Modding Tutorials",
    source_root="https://tutorials.sts2modding.com/",
    source_repository="https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials",
    mirror_dir=DOCS_ROOT / "tutorials.sts2modding.com",
    source_mode="live-html",
)
RITSULIB_SITE = SiteSpec(
    key="sts2-ritsulib.ritsukage.com",
    label="RitsuLib Guide",
    source_root="https://sts2-ritsulib.ritsukage.com/",
    source_repository="https://github.com/BAKAOLC/STS2-RitsuLib",
    mirror_dir=DOCS_ROOT / "sts2-ritsulib.ritsukage.com",
    source_mode="repository-markdown",
)
RITSULIB_BRANCH = "main"


@dataclass(frozen=True)
class Page:
    url: str
    route: str
    title: str
    path: str
    source_url: str | None = None


@dataclass
class SiteResult:
    spec: SiteSpec
    pages: list[Page] = field(default_factory=list)
    rendered: dict[Page, str] = field(default_factory=dict)
    failures: list[dict[str, str]] = field(default_factory=list)
    source_revision: str | None = None

    @property
    def succeeded(self) -> bool:
        return bool(self.pages) and len(self.rendered) == len(self.pages) and not self.failures


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
        attrs = {key: value or "" for key, value in attrs_raw}
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
            self.code_lang = next((name for name in classes if name != "highlight"), "")
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
        elif tag == "article":
            self.in_article = False
        elif tag in self.block_tags:
            self._newline()

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
        if self.parts and not self.parts[-1].endswith("\n"):
            self.parts.append("\n")

    def _blank(self) -> None:
        if not self.parts:
            return
        current = "".join(self.parts[-3:])
        if current.endswith("\n\n"):
            return
        self.parts.append("\n" if current.endswith("\n") else "\n\n")


def normalize_ws(value: str) -> str:
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def fetch_bytes(
    url: str,
    *,
    accept: str = "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8",
    timeout: int = 30,
    retries: int = 3,
) -> bytes:
    request = urllib.request.Request(
        url,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": accept,
            "Accept-Charset": "utf-8",
        },
    )
    last_error: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read()
        except Exception as exc:  # noqa: BLE001 - retain the actual network error.
            last_error = exc
            if attempt + 1 < retries:
                time.sleep(1 + attempt)
    raise RuntimeError(f"failed to fetch {url}: {last_error}")


def fetch_text(
    url: str,
    *,
    accept: str = "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8",
    timeout: int = 30,
    retries: int = 3,
) -> str:
    return fetch_bytes(url, accept=accept, timeout=timeout, retries=retries).decode(
        "utf-8",
        errors="strict",
    )


def discover_legacy_pages(home_html: str) -> list[Page]:
    collector = LinkCollector()
    collector.feed(home_html)

    seen: set[str] = set()
    pages: list[Page] = []
    for href, link_text in collector.links:
        absolute = urllib.parse.urljoin(LEGACY_SITE.source_root, href)
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
        path = posixpath.join(LEGACY_SITE.key, route, "index.md")
        pages.append(
            Page(
                url=urllib.parse.urljoin(LEGACY_SITE.source_root, route),
                route=route,
                title=title,
                path=path,
            )
        )
    return pages


def html_page_title(page_html: str, fallback: str) -> str:
    parser = TitleParser()
    parser.feed(page_html)
    return parser.title or fallback


def legacy_page_markdown(page: Page, page_html: str) -> str:
    title = html_page_title(page_html, page.title)
    parser = ArticleMarkdownParser()
    parser.feed(page_html)
    body = parser.get_markdown()
    if not body.strip():
        raise RuntimeError("rendered article body is empty")
    body = re.sub(rf"^#\s+{re.escape(title)}\s*\n+", "", body)
    return f"# {title}\n\n<!-- Source: {page.url} -->\n\n{body}"


def fetch_pages(
    pages: Iterable[Page],
    render: Callable[[Page], str],
) -> tuple[dict[Page, str], list[dict[str, str]]]:
    page_list = list(pages)
    rendered: dict[Page, str] = {}
    failure_by_page: dict[Page, dict[str, str]] = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_FETCH_WORKERS) as executor:
        futures = {executor.submit(render, page): page for page in page_list}
        for future in concurrent.futures.as_completed(futures):
            page = futures[future]
            try:
                rendered[page] = future.result()
            except Exception as exc:  # noqa: BLE001 - report every failed page together.
                failure_by_page[page] = {
                    "url": page.url,
                    "path": page.path,
                    "error": str(exc),
                }

    # WSL networking can briefly drop DNS or a single connection under a
    # burst of requests. Retry only failed pages serially before rejecting the
    # complete snapshot.
    if failure_by_page:
        time.sleep(2)
        for page in page_list:
            if page not in failure_by_page:
                continue
            try:
                rendered[page] = render(page)
                del failure_by_page[page]
            except Exception as exc:  # noqa: BLE001 - keep the final retry error.
                failure_by_page[page]["error"] = str(exc)

    failures = [failure_by_page[page] for page in page_list if page in failure_by_page]
    return rendered, failures


def render_legacy_site() -> SiteResult:
    result = SiteResult(spec=LEGACY_SITE)
    try:
        pages = discover_legacy_pages(fetch_text(LEGACY_SITE.source_root))
        if not pages:
            raise RuntimeError("no docs pages found from site navigation")
        result.pages = pages
        result.rendered, result.failures = fetch_pages(
            pages,
            lambda page: legacy_page_markdown(page, fetch_text(page.url)),
        )
    except Exception as exc:  # noqa: BLE001 - convert site-level errors to a report.
        result.failures.append(
            {
                "url": LEGACY_SITE.source_root,
                "path": LEGACY_SITE.key,
                "error": str(exc),
            }
        )
    return result


def strip_yaml_value(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
        return value[1:-1]
    return value


def markdown_page_title(markdown: str, fallback: str) -> str:
    lines = markdown.splitlines()
    titles: dict[str, str] = {}
    scalar_title = ""
    if lines and lines[0].strip() == "---":
        in_title = False
        for line in lines[1:]:
            if line.strip() == "---":
                break
            title_match = re.match(r"^title:\s*(.*)$", line)
            if title_match:
                in_title = True
                scalar_title = strip_yaml_value(title_match.group(1))
                continue
            if in_title:
                nested_match = re.match(r"^\s+([A-Za-z-]+):\s*(.+?)\s*$", line)
                if nested_match:
                    titles[nested_match.group(1)] = strip_yaml_value(nested_match.group(2))
                    continue
                if line and not line[0].isspace():
                    in_title = False

    english = titles.get("en", "")
    chinese = titles.get("zh-CN", "")
    if chinese and english and chinese != english:
        return f"{chinese} / {english}"
    if chinese or english or scalar_title:
        return chinese or english or scalar_title

    for line in lines:
        heading_match = re.match(r"^#\s+(.+?)\s*$", line)
        if heading_match:
            return heading_match.group(1)
    return fallback


def ritsulib_route(source_path: str) -> str:
    relative = PurePosixPath(source_path).relative_to("docs/pages")
    if relative.name == "index.md":
        parent = relative.parent.as_posix()
        return "" if parent == "." else parent.rstrip("/") + "/"
    return relative.with_suffix("").as_posix().rstrip("/") + "/"


def rewrite_ritsulib_links(
    markdown: str,
    page: Page,
    path_by_route: dict[str, str],
) -> str:
    link_pattern = re.compile(
        r"(?P<prefix>\]\()(?P<path>/guide(?:/[^)\s?#]*)?/?)(?P<anchor>#[^)\s]*)?(?=\))"
    )

    def replace(match: re.Match[str]) -> str:
        route = "/" + match.group("path").strip("/")
        target = path_by_route.get(route)
        if target is None:
            return match.group(0)
        relative = posixpath.relpath(target, posixpath.dirname(page.path))
        return f"{match.group('prefix')}{relative}{match.group('anchor') or ''}"

    return link_pattern.sub(replace, markdown)


def normalize_markdown_output(markdown: str) -> str:
    return "\n".join(line.rstrip() for line in markdown.splitlines()).rstrip() + "\n"


def resolve_git_revision() -> str:
    try:
        completed = subprocess.run(
            [
                "git",
                "ls-remote",
                RITSULIB_SITE.source_repository,
                f"refs/heads/{RITSULIB_BRANCH}",
            ],
            check=True,
            capture_output=True,
            text=True,
            timeout=10,
        )
    except FileNotFoundError as exc:
        raise RuntimeError("git is required to resolve the RitsuLib source revision") from exc
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("timed out while resolving the RitsuLib source revision") from exc
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or str(exc)).strip()
        raise RuntimeError(f"could not resolve the RitsuLib source revision: {detail}") from exc
    except OSError as exc:
        raise RuntimeError(f"could not run git to resolve the RitsuLib source revision: {exc}") from exc
    revision = completed.stdout.partition("\t")[0].strip().lower()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise RuntimeError("git returned an invalid RitsuLib source revision")
    return revision


def fetch_repository_archive(url: str) -> bytes:
    curl = shutil.which("curl")
    curl_error = "curl was not found"
    if curl:
        descriptor, temporary_name = tempfile.mkstemp(prefix="ninjaslayer-ritsulib-", suffix=".tar.gz")
        os.close(descriptor)
        temporary_path = Path(temporary_name)
        try:
            try:
                completed = subprocess.run(
                    [
                        curl,
                        "-4",
                        "--fail",
                        "--location",
                        "--silent",
                        "--show-error",
                        "--retry",
                        "5",
                        "--retry-all-errors",
                        "--retry-delay",
                        "2",
                        "--connect-timeout",
                        "15",
                        "--max-time",
                        "180",
                        "--user-agent",
                        USER_AGENT,
                        "--header",
                        "Accept: application/gzip,*/*;q=0.8",
                        "--output",
                        str(temporary_path),
                        url,
                    ],
                    check=True,
                    capture_output=True,
                    text=True,
                    timeout=210,
                )
                if completed.returncode == 0:
                    return temporary_path.read_bytes()
            except (subprocess.SubprocessError, OSError) as exc:
                if isinstance(exc, subprocess.CalledProcessError):
                    curl_error = (exc.stderr or exc.stdout or str(exc)).strip()
                else:
                    curl_error = str(exc)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()

    try:
        return fetch_bytes(
            url,
            accept="application/gzip,*/*;q=0.8",
            timeout=180,
            retries=3,
        )
    except Exception as exc:  # noqa: BLE001 - include both transport errors.
        raise RuntimeError(f"curl failed ({curl_error}); urllib fallback failed ({exc})") from exc


def load_ritsulib_markdown() -> tuple[str, dict[str, str]]:
    revision = resolve_git_revision()
    archive_url = f"https://codeload.github.com/BAKAOLC/STS2-RitsuLib/tar.gz/{revision}"
    archive_payload = fetch_repository_archive(archive_url)

    markdown_by_source: dict[str, str] = {}
    with tarfile.open(fileobj=io.BytesIO(archive_payload), mode="r:gz") as archive:
        for member in archive.getmembers():
            marker = "/docs/pages/"
            if not member.isfile() or marker not in member.name or not member.name.endswith(".md"):
                continue
            source_path = "docs/pages/" + member.name.split(marker, 1)[1]
            extracted = archive.extractfile(member)
            if extracted is None:
                raise RuntimeError(f"could not read {source_path} from repository archive")
            markdown_by_source[source_path] = extracted.read().decode("utf-8", errors="strict")
    return revision, markdown_by_source


def render_ritsulib_site() -> SiteResult:
    result = SiteResult(spec=RITSULIB_SITE)
    try:
        revision, markdown_by_source = load_ritsulib_markdown()

        source_paths = sorted(markdown_by_source)
        if not source_paths:
            raise RuntimeError("no Markdown pages found under docs/pages")

        def source_order(source_path: str) -> tuple[int, str]:
            relative = PurePosixPath(source_path).relative_to("docs/pages").as_posix()
            if relative == "index.md":
                return (0, relative)
            if relative == "guide/index.md":
                return (1, relative)
            return (2, relative)

        source_paths.sort(key=source_order)
        source_web_root = f"{RITSULIB_SITE.source_repository}/blob/{revision}/"

        pages: list[Page] = []
        rendered: dict[Page, str] = {}
        for source_path in source_paths:
            route = ritsulib_route(source_path)
            fallback = PurePosixPath(source_path).stem.replace("-", " ").title()
            markdown = markdown_by_source[source_path]
            markdown = markdown.replace("\r\n", "\n").replace("\r", "\n")
            if not markdown.strip():
                raise RuntimeError(f"source Markdown is empty: {source_path}")
            page = Page(
                url=urllib.parse.urljoin(RITSULIB_SITE.source_root, route),
                route=route,
                title=markdown_page_title(markdown, fallback),
                path=posixpath.join(RITSULIB_SITE.key, route, "index.md"),
                source_url=source_web_root + urllib.parse.quote(source_path, safe="/"),
            )
            pages.append(page)
            rendered[page] = markdown.rstrip() + "\n"

        path_by_route = {
            "/" + page.route.strip("/"): page.path
            for page in pages
        }
        rendered = {
            page: normalize_markdown_output(
                rewrite_ritsulib_links(markdown, page, path_by_route)
            )
            for page, markdown in rendered.items()
        }

        result.pages = pages
        result.rendered = rendered
        result.source_revision = revision
    except Exception as exc:  # noqa: BLE001 - convert site-level errors to a report.
        result.failures.append(
            {
                "url": RITSULIB_SITE.source_repository,
                "path": RITSULIB_SITE.key,
                "error": str(exc),
            }
        )
    return result


def page_manifest_entry(page: Page) -> dict[str, str]:
    entry = {
        "url": page.url,
        "route": page.route,
        "title": page.title,
        "path": page.path,
    }
    if page.source_url:
        entry["sourceUrl"] = page.source_url
    return entry


def manifest_text(result: SiteResult, synced_at: str) -> str:
    manifest: dict[str, object] = {
        "source": result.spec.source_root,
        "sourceRepository": result.spec.source_repository,
        "sourceMode": result.spec.source_mode,
        "syncedAt": synced_at,
        "pageCount": len(result.pages),
        "pages": [page_manifest_entry(page) for page in result.pages],
        "failureCount": 0,
        "failures": [],
    }
    if result.source_revision:
        manifest["sourceRevision"] = result.source_revision
    return json.dumps(manifest, ensure_ascii=False, indent=2) + "\n"


def replace_mirror(result: SiteResult, synced_at: str) -> None:
    target = result.spec.mirror_dir
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{target.name}.sync-", dir=target.parent))
    backup: Path | None = None
    try:
        for page in result.pages:
            relative_path = Path(page.path).relative_to(result.spec.key)
            output_path = staging / relative_path
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(result.rendered[page], encoding="utf-8", newline="\n")
        (staging / "_sync_manifest.json").write_text(
            manifest_text(result, synced_at),
            encoding="utf-8",
            newline="\n",
        )

        if target.exists():
            backup = Path(tempfile.mkdtemp(prefix=f".{target.name}.backup-", dir=target.parent))
            backup.rmdir()
            target.rename(backup)
        try:
            staging.rename(target)
        except Exception:
            if backup and backup.exists() and not target.exists():
                backup.rename(target)
            raise
        if backup and backup.exists():
            shutil.rmtree(backup)
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def reference_text(results: Iterable[SiteResult]) -> str:
    lines = [
        "# STS2 Modding Tutorial Mirrors",
        "",
        "Update from WSL or Linux with:",
        "",
        "```bash",
        "python3 Docs/_sync_tutorials.py",
        "```",
        "",
        "On Windows, the existing wrapper remains available:",
        "",
        "```powershell",
        ".\\Docs\\Sync-Sts2Tutorials.ps1",
        "```",
        "",
    ]
    for result in results:
        lines.extend(
            [
                f"## {result.spec.label}",
                "",
                f"Source: {result.spec.source_root}",
                f"Repository: {result.spec.source_repository}",
                f"Manifest: [{result.spec.key}/_sync_manifest.json]({result.spec.key}/_sync_manifest.json)",
                "",
                "### Pages",
                "",
            ]
        )
        lines.extend(f"- [{page.title}]({page.path})" for page in result.pages)
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def atomic_write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    os.close(descriptor)
    temporary_path = Path(temporary_name)
    try:
        temporary_path.write_text(content, encoding="utf-8", newline="\n")
        os.replace(temporary_path, path)
    finally:
        if temporary_path.exists():
            temporary_path.unlink()


def report_result(result: SiteResult, *, dry_run: bool) -> None:
    action = "Dry run" if dry_run else "Ready"
    print(
        f"[{result.spec.key}] {action}: discovered {len(result.pages)}; "
        f"renderable {len(result.rendered)}; failures {len(result.failures)}"
    )
    for failure in result.failures:
        print(
            f"[{result.spec.key}] {failure['url']}: {failure['error']}",
            file=sys.stderr,
        )


def sync(dry_run: bool = False) -> int:
    # Fetch the single GitHub snapshot before the legacy site's larger burst of
    # requests, then present/write the mirrors in their established order.
    builders = (render_ritsulib_site, render_legacy_site)
    fetched: dict[str, SiteResult] = {}
    for builder in builders:
        result = builder()
        fetched[result.spec.key] = result
        report_result(result, dry_run=dry_run)

    results = [fetched[LEGACY_SITE.key], fetched[RITSULIB_SITE.key]]

    if not all(result.succeeded for result in results):
        print("No mirror directories were changed because at least one source failed.", file=sys.stderr)
        return 1
    if dry_run:
        return 0

    synced_at = dt.datetime.now(dt.timezone.utc).astimezone().isoformat(timespec="seconds")
    for result in results:
        replace_mirror(result, synced_at)
        print(f"[{result.spec.key}] Synced {len(result.pages)} pages to {result.spec.mirror_dir}")
    atomic_write_text(REFERENCE_PATH, reference_text(results))
    print(f"Updated {REFERENCE_PATH}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="discover and render every site without writing files",
    )
    args = parser.parse_args()
    return sync(dry_run=args.dry_run)


if __name__ == "__main__":
    raise SystemExit(main())
