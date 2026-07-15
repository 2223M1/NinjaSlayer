#!/usr/bin/env python3
"""Search the private Ninja Slayer corpus with versioned alias expansion."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")


MAX_LINE_CHARS = 260


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def find_repo_root(explicit: Path | None) -> Path:
    if explicit:
        root = explicit.expanduser().resolve()
        if (root / "Docs" / "lore" / "sources.json").is_file():
            return root
        raise RuntimeError(f"Not a NinjaSlayer lore repository: {root}")
    script = Path(__file__).resolve()
    candidates = [Path.cwd(), *Path.cwd().parents, *script.parents]
    for candidate in candidates:
        if (candidate / "Docs" / "lore" / "sources.json").is_file():
            return candidate
    raise RuntimeError("Could not locate a repository containing Docs/lore/sources.json; pass --repo-root")


def validate_cache(repo_root: Path, manifest: dict) -> None:
    source_root = Path(manifest.get("source_root", ""))
    stale: list[str] = []
    source_manifest = repo_root / "Docs" / "lore" / "sources.json"
    if not source_manifest.is_file() or sha256_file(source_manifest) != manifest.get("source_manifest_sha256"):
        stale.append("changed Docs/lore/sources.json")
    for book in manifest.get("books", []):
        path = source_root / book["filename"]
        if not path.is_file():
            stale.append(f"missing {book['filename']}")
        elif sha256_file(path) != book.get("sha256"):
            stale.append(f"changed {book['filename']}")
        chunk_dir = repo_root / ".lore-cache" / "corpus" / book["id"]
        if not all((chunk_dir / chunk["file"]).is_file() for chunk in book.get("chunks", [])):
            stale.append(f"incomplete cache for {book['id']}")
    if stale:
        details = "; ".join(dict.fromkeys(stale))
        raise RuntimeError(
            f"Lore cache is stale ({details}). Rebuild with build_corpus.py --repo-root \"{repo_root}\""
        )


def expand_query(query: str, aliases: dict) -> list[str]:
    terms = [query]
    folded_query = query.casefold()
    for group in aliases.get("groups", []):
        forms = [group.get("canonical", ""), *group.get("aliases", [])]
        folded_forms = [form.casefold() for form in forms if form]
        if any(folded_query == form or folded_query in form or form in folded_query for form in folded_forms):
            terms.extend(form for form in forms if form)
    return list(dict.fromkeys(terms))


def clip(text: str) -> str:
    text = text.rstrip("\n")
    if len(text) <= MAX_LINE_CHARS:
        return text
    return text[: MAX_LINE_CHARS - 1] + "…"


def read_metadata(first_line: str) -> dict:
    match = re.match(r"<!-- lore-meta: (.+) -->\s*$", first_line)
    if not match:
        raise RuntimeError("Corpus chunk has invalid metadata header")
    return json.loads(match.group(1))


def search_chunk(path: Path, query: str, terms: list[str], context: int, pattern: re.Pattern | None) -> list[dict]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        return []
    metadata = read_metadata(lines[0])
    folded_query = query.casefold()
    folded_terms = [(term, term.casefold()) for term in terms]
    matches: list[dict] = []
    for index, line in enumerate(lines[3:], start=3):
        folded_line = line.casefold()
        if pattern is not None:
            regex_hits = list(pattern.finditer(line))
            hits = [(query, query.casefold())] if regex_hits else []
        else:
            regex_hits = []
            hits = [(term, folded) for term, folded in folded_terms if folded and folded in folded_line]
        if not hits:
            continue
        exact_count = len(regex_hits) if pattern is not None else (folded_line.count(folded_query) if folded_query else 0)
        alias_count = sum(folded_line.count(folded) for _, folded in hits if folded != folded_query)
        score = exact_count * 100 + alias_count * 20 + len({folded for _, folded in hits}) * 3
        start = max(3, index - context)
        end = min(len(lines), index + context + 1)
        matches.append({
            "score": score,
            "part": metadata["part"],
            "book_id": metadata["book_id"],
            "book_title": metadata["book_title"],
            "chapter": metadata["chapter"],
            "chunk": metadata["chunk"],
            "line": index + 1,
            "matched_terms": list(dict.fromkeys(term for term, _ in hits)),
            "context": [
                {"line": line_index + 1, "text": clip(lines[line_index]), "match": line_index == index}
                for line_index in range(start, end)
            ],
        })
    return matches


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--query", required=True)
    parser.add_argument("--book", choices=("part1", "part2", "part3"))
    parser.add_argument("--limit", type=int, default=12)
    parser.add_argument("--context", type=int, default=2)
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--regex", action="store_true", help="Treat --query as a regular expression; alias expansion is disabled")
    parser.add_argument("--repo-root", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if not args.query.strip():
        parser.error("--query cannot be empty")
    if not 1 <= args.limit <= 100:
        parser.error("--limit must be between 1 and 100")
    if not 0 <= args.context <= 8:
        parser.error("--context must be between 0 and 8")

    repo_root = find_repo_root(args.repo_root)
    manifest_path = repo_root / ".lore-cache" / "manifest.json"
    if not manifest_path.is_file():
        raise RuntimeError(f"Lore cache is missing. Run build_corpus.py --repo-root \"{repo_root}\"")
    manifest = load_json(manifest_path)
    validate_cache(repo_root, manifest)
    aliases = load_json(repo_root / "Docs" / "lore" / "aliases.json")
    query = args.query.strip()
    terms = [query] if args.regex else expand_query(query, aliases)
    pattern = re.compile(query, re.IGNORECASE) if args.regex else None

    results: list[dict] = []
    for book in manifest.get("books", []):
        if args.book and book["id"] != args.book:
            continue
        chunk_dir = repo_root / ".lore-cache" / "corpus" / book["id"]
        for chunk in book.get("chunks", []):
            results.extend(search_chunk(chunk_dir / chunk["file"], query, terms, args.context, pattern))
    results.sort(key=lambda item: (-item["score"], item["book_id"], item["chunk"], item["line"]))
    results = results[: args.limit]

    if args.json:
        print(json.dumps({"query": query, "expanded_terms": terms, "results": results}, ensure_ascii=False, indent=2))
        return 0

    print("查询：" + query)
    print("扩展：" + "、".join(terms))
    if not results:
        print("未找到匹配结果。")
        return 0
    for number, result in enumerate(results, start=1):
        citation = f"[{result['part']}｜{result['chapter']}｜{result['chunk']}:{result['line']}]"
        print(f"\n{number}. {citation}  score={result['score']}")
        for context_line in result["context"]:
            marker = ">" if context_line["match"] else " "
            print(f" {marker} {context_line['line']:>4}: {context_line['text']}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, ValueError, json.JSONDecodeError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
