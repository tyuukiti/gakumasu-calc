"""
サポートカード画像取得スクリプト
seesaawiki.jp から画像をダウンロードし Data/Images/ に保存する。
30秒間隔でアクセスし、サーバに負荷をかけない。
"""

import os
import re
import sys
import time
import urllib.request
import urllib.parse

WIKI_URL = "https://seesaawiki.jp/gakumasu/d/%A5%B5%A5%DD%A1%BC%A5%C8%A5%AB%A1%BC%A5%C9%B0%EC%CD%F7"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Data", "Images")
DELAY_SECONDS = 30

# HTMLからカード情報を抽出する正規表現
CARD_PATTERN = re.compile(
    r'<img src="(https://image\d+\.seesaawiki\.jp/g/u/gakumasu/[^"]+)"[^/]*/></a></td><td><a[^>]*>([^<]+)</a>(?:<br />\(([^)]+)\))?'
)


def fetch_page(url):
    """ページHTMLを取得 (seesaawikiはEUC-JP)"""
    req = urllib.request.Request(url, headers={
        "User-Agent": "Mozilla/5.0 (gakumasu-tool image fetcher; polite 30s delay)"
    })
    with urllib.request.urlopen(req, timeout=30) as resp:
        raw = resp.read()
    for enc in ["euc-jp", "utf-8", "shift_jis"]:
        try:
            return raw.decode(enc)
        except (UnicodeDecodeError, LookupError):
            continue
    return raw.decode("utf-8", errors="replace")


def get_full_size_url(thumb_url):
    """サムネイルURL (-s) からフルサイズURLに変換"""
    return re.sub(r'-s\.', '.', thumb_url)


def download_image(url, filepath):
    """画像をダウンロード"""
    req = urllib.request.Request(url, headers={
        "User-Agent": "Mozilla/5.0 (gakumasu-tool image fetcher; polite 30s delay)"
    })
    with urllib.request.urlopen(req, timeout=30) as resp:
        data = resp.read()
    with open(filepath, "wb") as f:
        f.write(data)
    return len(data)


def sanitize_filename(name):
    """カード名をファイル名に変換"""
    name = re.sub(r'[<>:"/\\|?*]', '_', name)
    name = re.sub(r'[♡♪★☆〜]', '_', name)
    name = name.strip('. ')
    if len(name) > 80:
        name = name[:80]
    return name


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    # ページ取得
    print(f"ページ取得中: {WIKI_URL}")
    html = fetch_page(WIKI_URL)

    # 正規表現でカード情報を抽出
    matches = CARD_PATTERN.findall(html)
    print(f"カード数: {len(matches)}枚")

    if not matches:
        print("カードが見つかりませんでした")
        return

    mapping_lines = []
    downloaded = 0
    skipped = 0

    for i, (thumb_url, card_name, card_id) in enumerate(matches):
        full_url = get_full_size_url(thumb_url)
        ext = os.path.splitext(urllib.parse.urlparse(full_url).path)[1] or ".png"

        # ファイル名: カードID があればそれを使う、なければカード名
        if card_id:
            filename = f"{card_id}{ext}"
        else:
            filename = f"{sanitize_filename(card_name)}{ext}"

        filepath = os.path.join(OUTPUT_DIR, filename)

        # マッピング記録
        mapping_lines.append(f"{card_id}\t{card_name}\t{filename}")

        # 既にダウンロード済みならスキップ
        if os.path.exists(filepath):
            print(f"[{i+1}/{len(matches)}] スキップ (既存): {card_name} ({card_id})")
            skipped += 1
            continue

        print(f"[{i+1}/{len(matches)}] ダウンロード: {card_name} ({card_id})")

        try:
            size = download_image(full_url, filepath)
            print(f"  → {filename} ({size:,} bytes)")
            downloaded += 1
        except Exception as e:
            print(f"  エラー: {e}")
            # サムネイルURLでリトライ
            try:
                size = download_image(thumb_url, filepath)
                print(f"  → サムネイルで保存: {filename} ({size:,} bytes)")
                downloaded += 1
            except Exception as e2:
                print(f"  リトライ失敗: {e2}")

        # 30秒待機 (最後以外)
        if i < len(matches) - 1:
            print(f"  待機 {DELAY_SECONDS}秒...")
            time.sleep(DELAY_SECONDS)

    # マッピングファイル保存
    mapping_path = os.path.join(OUTPUT_DIR, "_mapping.tsv")
    with open(mapping_path, "w", encoding="utf-8") as f:
        f.write("card_id\tcard_name\tfilename\n")
        f.write("\n".join(mapping_lines))

    print(f"\n完了: ダウンロード {downloaded}枚, スキップ {skipped}枚")
    print(f"マッピング: {mapping_path}")
    print(f"所要時間目安: {downloaded * DELAY_SECONDS // 60}分")


if __name__ == "__main__":
    main()
