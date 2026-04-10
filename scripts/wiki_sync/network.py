"""ネットワークアクセス"""
import urllib.request
from pathlib import Path


def fetch_page(url: str) -> str:
    """Wikiページを取得してHTMLを返す"""
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    resp = urllib.request.urlopen(req)
    return resp.read().decode("euc-jp", errors="replace")


def download_file(url: str, filepath: Path):
    """URLからファイルをダウンロード"""
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    resp = urllib.request.urlopen(req)
    filepath.parent.mkdir(parents=True, exist_ok=True)
    with open(filepath, "wb") as f:
        f.write(resp.read())
