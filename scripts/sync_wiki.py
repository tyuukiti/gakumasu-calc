"""
Seesaa Wiki とYAMLデータの差分同期スクリプト。

- 新規カード: Wiki から取得して YAML に追加 + 画像ダウンロード
- 既存カード: 凸別値のみ更新
- 削除は行わない (安全側)

使い方:
  python scripts/sync_wiki.py                  # 差分チェック + 同期
  python scripts/sync_wiki.py --dry-run        # 確認のみ (書き込みなし)
  python scripts/sync_wiki.py --update-only    # 既存カードの値更新のみ
  python scripts/sync_wiki.py --new-only       # 新規カード追加のみ
  python scripts/sync_wiki.py --images-only    # 画像未取得カードの画像のみ取得 (YAMLは変更しない)
  python scripts/sync_wiki.py --delay 10       # リクエスト間隔を変更
  python scripts/sync_wiki.py --debug          # 詳細デバッグ出力
"""
import sys
import os
import time
import argparse

sys.stdout.reconfigure(encoding="utf-8")

from wiki_sync.constants import DATA_DIR, IMAGE_DIR, DEFAULT_DELAY, normalize_name
from wiki_sync.network import download_file
from wiki_sync.parsers import fetch_card_tags, fetch_item_effects, parse_list_page, parse_detail_page
from wiki_sync.card_builder import build_new_card, classify_and_match
from wiki_sync.yaml_io import (
    load_yaml_cards, write_yaml_with_flow_values,
    load_synced, save_synced,
    load_mapping, append_mapping, next_card_id,
)


def _image_already_downloaded(card_name: str, norm_image_mapping: dict) -> bool:
    """画像マッピングに登録済みで、かつ実ファイルも存在するなら True。

    マッピング未登録のカード（新規登録直後など）の場合に、
    `IMAGE_DIR / ""` がディレクトリそのものを指して exists() が True を返してしまう
    バグを避けるためのヘルパー。
    """
    entry = norm_image_mapping.get(normalize_name(card_name))
    if not entry:
        return False
    filename = entry.get("filename", "")
    return bool(filename) and (IMAGE_DIR / filename).exists()


def _fetch_missing_images(args):
    """画像未取得のカードに対してだけ画像を取得する。YAMLは変更しない。

    過去のバグ（マッピング未登録時に IMAGE_DIR / "" を見て exists() が True を返すため
    新規カードでも画像取得がスキップされる問題）により、既に _synced.txt に登録済みで
    画像だけ落ちていないカードがある場合の救済モード。
    """
    print("=== 画像未取得カードの取得 ===")
    if args.dry_run:
        print("  [DRY-RUN]")
    print()

    # 1. 画像マッピング読み込み
    image_mapping = load_mapping()
    norm_image_mapping = {normalize_name(k): v for k, v in image_mapping.items()}

    # 2. 一覧ページ取得
    print("一覧ページを取得中...")
    wiki_entries = parse_list_page()
    print(f"  Wikiカード: {len(wiki_entries)} 枚")

    # 3. 画像未取得カードを抽出
    missing = [
        e for e in wiki_entries
        if e.detail_url and not _image_already_downloaded(e.name, norm_image_mapping)
    ]
    print(f"  画像未取得: {len(missing)} 枚")
    for e in missing:
        print(f"    - {e.name}")
    print()

    if not missing:
        print("取得対象なし。終了。")
        return

    # 4. 各カードの画像取得
    img_count = 0
    for i, entry in enumerate(missing):
        print(f"[{i+1}/{len(missing)}] {entry.name}")
        try:
            detail = parse_detail_page(entry.detail_url, debug=args.debug)
            if detail is None:
                print("  ⚠ 詳細ページ解析失敗")
            else:
                img_url = detail.get("image_url")
                if not img_url:
                    print("  ⚠ 画像URLが見つかりません")
                else:
                    ext = img_url.rsplit(".", 1)[-1].lower()
                    if ext not in ("png", "jpg", "jpeg"):
                        ext = "png"
                    img_filename = f"{entry.wiki_id}.{ext}"
                    img_path = IMAGE_DIR / img_filename
                    print(f"  画像: {img_filename}")
                    if not args.dry_run:
                        download_file(img_url, img_path)
                        append_mapping(entry.wiki_id, entry.name, img_filename)
                        img_count += 1
        except Exception as e:
            print(f"  ✗ エラー: {e}")

        if i < len(missing) - 1:
            time.sleep(args.delay)

    print(f"\n=== 完了: 画像 {img_count} 件取得 ===")


def _update_item_effects_only(args):
    """タグページからアイテム効果のみ取得・更新する"""
    print("=== アイテム効果のみ更新 ===")
    if args.dry_run:
        print("  [DRY-RUN]")
    print()

    # 1. タグページからアイテム効果取得
    print("タグ情報を取得中...")
    _, tag_page_html = fetch_card_tags()
    if not tag_page_html:
        print("  ⚠ タグページ取得失敗")
        return
    item_effects_map = fetch_item_effects(tag_page_html)
    print(f"  アイテム効果: {len(item_effects_map)} 件")
    print()

    # 2. 既存YAML読み込み
    yaml_files = {
        "SSR": str(DATA_DIR / "ssr_cards.yaml"),
        "SR": str(DATA_DIR / "sr_cards.yaml"),
        "R": str(DATA_DIR / "r_cards.yaml"),
    }
    all_by_file: dict[str, list] = {}
    name_to_info: dict[str, dict] = {}
    for rarity, filepath in yaml_files.items():
        if os.path.exists(filepath):
            cards = load_yaml_cards(filepath)
            all_by_file[filepath] = cards
            for card in cards:
                name_to_info[card["name"]] = {"card": card, "file": filepath}

    # 3. アイテム効果の比較・更新 (1アイテム = 複数 effect の場合あり: stat上昇 + Pドリンク等)
    update_count = 0
    modified_files = set()

    def _eff_key(e: dict) -> tuple:
        """item effect 同一性判定キー。同 trigger かつ同 value_type かつ同 trigger_target なら同じ effect 扱い"""
        return (
            e.get("trigger"),
            e.get("value_type"),
            e.get("trigger_target"),  # trigger_count_bonus を区別
        )

    def _eff_label(e: dict) -> str:
        """ログ用のラベル"""
        v = e.get("values", [0])[-1]
        if e.get("value_type") == "trigger_count_bonus":
            return f"{e.get('value_type')} target={e.get('trigger_target')} scales={e.get('scales_with')} x{int(v)}"
        return f"{e.get('trigger')} {e.get('stat')}+{int(v)}"

    for card_name, item_effs in item_effects_map.items():
        norm_name = normalize_name(card_name)
        info = None
        for name, i in name_to_info.items():
            if normalize_name(name) == norm_name:
                info = i
                break
        if info is None:
            continue

        # trigger_count_bonus は常にアイテム由来なので source 属性が未設定でもマッチさせる
        existing_item_relevant = [
            e for e in info["card"]["effects"]
            if e.get("source") == "item" or e.get("value_type") == "trigger_count_bonus"
        ]
        existing_by_key = {_eff_key(e): e for e in existing_item_relevant}
        new_by_key = {_eff_key(e): e for e in item_effs}

        # 追加: Wiki にあって既存に無い effect
        for k, new_eff in new_by_key.items():
            if k in existing_by_key:
                continue
            info["card"]["effects"].append(new_eff)
            modified_files.add(info["file"])
            update_count += 1
            print(f"  + {info['card']['name']}: {_eff_label(new_eff)}")

        # 更新: 同 key の既存 effect の値が変わっていれば反映
        for k, new_eff in new_by_key.items():
            old = existing_by_key.get(k)
            if old is None:
                continue
            item_changed = False
            for key in ("trigger", "stat", "values", "value_type", "condition", "max_count",
                        "trigger_target", "scales_with"):
                new_val = new_eff.get(key)
                old_val = old.get(key)
                if new_val != old_val:
                    if new_val is not None:
                        old[key] = new_val
                    elif key in old:
                        del old[key]
                    item_changed = True
            if item_changed:
                modified_files.add(info["file"])
                update_count += 1
                print(f"  ✓ {info['card']['name']}: {_eff_label(old)}")

    if update_count == 0:
        print("  変更なし")
    else:
        print(f"\n  更新: {update_count} 件")

    # 4. ファイル保存
    if modified_files and not args.dry_run:
        print("\nファイル保存中...")
        for filepath in modified_files:
            write_yaml_with_flow_values(filepath, all_by_file[filepath])
            print(f"  保存: {filepath}")

    print("\n=== 完了 ===")


def main():
    parser = argparse.ArgumentParser(description="Wiki差分同期")
    parser.add_argument("--dry-run", action="store_true", help="書き込みしない")
    parser.add_argument("--update-only", action="store_true", help="既存カードの値更新のみ")
    parser.add_argument("--new-only", action="store_true", help="新規カード追加のみ")
    parser.add_argument("--force", action="store_true", help="更新済み記録を無視して全件更新")
    parser.add_argument("--item-only", action="store_true", help="アイテム効果のみ更新")
    parser.add_argument("--images-only", action="store_true", help="画像未取得カードの画像のみ取得")
    parser.add_argument("--delay", type=int, default=DEFAULT_DELAY, help="リクエスト間隔(秒)")
    parser.add_argument("--debug", action="store_true", help="詳細デバッグ出力")
    args = parser.parse_args()

    if args.item_only:
        _update_item_effects_only(args)
        return

    if args.images_only:
        _fetch_missing_images(args)
        return

    print("=== Wiki 差分同期 ===")
    if args.dry_run:
        print("  [DRY-RUN]")
    if args.force:
        print("  [FORCE: 全件更新]")
    print()

    # 1. タグ情報 + アイテム効果取得
    print("タグ情報を取得中...")
    card_tag_map, tag_page_html = fetch_card_tags()
    item_effects_map: dict[str, dict] = {}
    if tag_page_html:
        item_effects_map = fetch_item_effects(tag_page_html)
        print(f"  アイテム効果: {len(item_effects_map)} 件")
    print(f"  タグ情報: {len(card_tag_map)} 件 (skill/exam_item/produce_item)")
    if card_tag_map:
        time.sleep(args.delay)

    # 2. Wiki 一覧ページ取得
    print("一覧ページを取得中...")
    wiki_entries = parse_list_page()
    print(f"  Wikiカード: {len(wiki_entries)} 枚")

    # 3. 既存YAML読み込み
    yaml_files = {
        "SSR": str(DATA_DIR / "ssr_cards.yaml"),
        "SR": str(DATA_DIR / "sr_cards.yaml"),
        "R": str(DATA_DIR / "r_cards.yaml"),
    }
    existing_cards: dict[str, dict] = {}  # {name: {card, file}}
    all_by_file: dict[str, list] = {}
    for rarity, filepath in yaml_files.items():
        if os.path.exists(filepath):
            cards = load_yaml_cards(filepath)
            all_by_file[filepath] = cards
            for card in cards:
                existing_cards[card["name"]] = {"card": card, "file": filepath}
        else:
            all_by_file[filepath] = []

    print(f"  既存カード: {len(existing_cards)} 枚")

    # 4. 画像マッピング読み込み
    image_mapping = load_mapping()
    norm_image_mapping = {normalize_name(k): v for k, v in image_mapping.items()}

    # 5. 差分検出 (名前の正規化で表記揺れを吸収)
    synced = load_synced() if not args.force else set()

    norm_to_existing: dict[str, str] = {}
    for name in existing_cards:
        norm_to_existing[normalize_name(name)] = name

    new_entries = []
    update_entries = []
    skipped_count = 0

    for entry in wiki_entries:
        norm_name = normalize_name(entry.name)
        if norm_name in norm_to_existing:
            entry.name = norm_to_existing[norm_name]
            if entry.detail_url:
                if entry.name in synced:
                    skipped_count += 1
                else:
                    update_entries.append(entry)
        else:
            if entry.detail_url:
                new_entries.append(entry)

    print(f"\n  新規: {len(new_entries)} 枚")
    for e in new_entries:
        print(f"    + {e.rarity} {e.name}")

    print(f"  更新対象: {len(update_entries)} 枚")
    if skipped_count > 0:
        print(f"  スキップ (更新済み): {skipped_count} 枚")
    print()

    if not new_entries and not update_entries:
        print("差分なし。終了。")
        return

    # 6. 処理対象を決定
    to_process = []
    if not args.update_only:
        for e in new_entries:
            to_process.append(("new", e))
    if not args.new_only:
        for e in update_entries:
            to_process.append(("update", e))

    if not to_process:
        print("処理対象なし。終了。")
        return

    # 7. 各カード処理
    new_count = 0
    update_count = 0
    img_count = 0
    modified_files = set()

    for i, (action, entry) in enumerate(to_process):
        label = "NEW" if action == "new" else "UPD"
        print(f"[{i+1}/{len(to_process)}] [{label}] {entry.name}")

        try:
            detail = parse_detail_page(entry.detail_url, debug=args.debug)
            if detail is None:
                print("  ⚠ 詳細ページ解析失敗")
                continue

            if action == "new":
                rarity = entry.rarity
                filepath = yaml_files[rarity]
                # Wiki一覧ページのIDを優先使用、なければ連番採番
                card_id = entry.wiki_id if entry.wiki_id else next_card_id(all_by_file[filepath], rarity)
                existing_ids = {c["id"] for c in all_by_file[filepath]}
                if card_id in existing_ids:
                    print(f"  ⚠ ID重複: {card_id}, 新ID採番")
                    card_id = next_card_id(all_by_file[filepath], rarity)

                card_tag = card_tag_map.get(normalize_name(entry.name), "none")
                card = build_new_card(card_id, entry, detail, detail.get("abilities", []), tag=card_tag, debug=args.debug)

                # アイテム効果をマージ (複数 effect の場合あり)
                item_effs = item_effects_map.get(normalize_name(entry.name), [])
                for item_eff in item_effs:
                    card["effects"].append(item_eff)
                    v = item_eff.get("values", [0])[-1]
                    if item_eff.get("value_type") == "trigger_count_bonus":
                        print(f"  + アイテム効果: {item_eff['value_type']} target={item_eff.get('trigger_target')} scales={item_eff.get('scales_with')} x{int(v)}")
                    else:
                        print(f"  + アイテム効果: {item_eff['trigger']} {item_eff['stat']}+{int(v)}")

                if not card["effects"]:
                    print(f"  ⚠ effects が空です (アビリティ取得失敗)")
                else:
                    tag_info = f", tag={card_tag}" if card_tag else ""
                    print(f"  id={card_id}, type={card['type']}, plan={card['plan']}, effects={len(card['effects'])}{tag_info}")
                    if not args.dry_run:
                        all_by_file[filepath].append(card)
                        modified_files.add(filepath)
                    new_count += 1

                # 画像ダウンロード
                if detail.get("image_url") and not _image_already_downloaded(entry.name, norm_image_mapping):
                    img_url = detail["image_url"]
                    ext = img_url.rsplit(".", 1)[-1].lower()
                    if ext not in ("png", "jpg", "jpeg"):
                        ext = "png"
                    img_filename = f"{entry.wiki_id}.{ext}"
                    img_path = IMAGE_DIR / img_filename
                    print(f"  画像: {img_filename}")
                    if not args.dry_run:
                        download_file(img_url, img_path)
                        append_mapping(entry.wiki_id, entry.name, img_filename)
                        img_count += 1

            elif action == "update":
                info = existing_cards[entry.name]
                abilities = detail.get("abilities", [])
                support_events = detail.get("support_events", [])
                value_updated = False
                if abilities or support_events:
                    card_type = info["card"].get("type", "vo")
                    default_stat = "all" if card_type == "as" else card_type
                    value_updated, logs = classify_and_match(
                        info["card"]["effects"], abilities,
                        default_stat=default_stat,
                        support_events=support_events,
                    )
                    for log in logs:
                        print(log)
                    if value_updated:
                        update_count += 1
                        modified_files.add(info["file"])
                        print("  → 更新あり")
                    else:
                        print("  → 変更なし")
                else:
                    print("  - テーブルなし")

                # アイテム効果をマージ (1アイテム = 複数 effect の場合あり)
                item_effs = item_effects_map.get(normalize_name(entry.name), [])
                if item_effs:
                    def _eff_key2(e: dict) -> tuple:
                        return (e.get("trigger"), e.get("value_type"), e.get("trigger_target"))

                    def _eff_label2(e: dict) -> str:
                        v = e.get("values", [0])[-1]
                        if e.get("value_type") == "trigger_count_bonus":
                            return f"{e.get('value_type')} target={e.get('trigger_target')} scales={e.get('scales_with')} x{int(v)}"
                        return f"{e.get('trigger')} {e.get('stat')}+{int(v)}"

                    # trigger_count_bonus は常にアイテム由来なので source 属性が未設定でもマッチさせる
                    existing_item_relevant = [
                        e for e in info["card"]["effects"]
                        if e.get("source") == "item" or e.get("value_type") == "trigger_count_bonus"
                    ]
                    existing_by_key = {_eff_key2(e): e for e in existing_item_relevant}

                    any_change = False
                    for new_eff in item_effs:
                        k = _eff_key2(new_eff)
                        old = existing_by_key.get(k)
                        if old is None:
                            info["card"]["effects"].append(new_eff)
                            modified_files.add(info["file"])
                            print(f"  + アイテム効果: {_eff_label2(new_eff)}")
                            any_change = True
                        else:
                            changed = False
                            for key in ("trigger", "stat", "values", "value_type", "condition", "max_count",
                                        "trigger_target", "scales_with"):
                                new_val = new_eff.get(key)
                                old_val = old.get(key)
                                if new_val != old_val:
                                    if new_val is not None:
                                        old[key] = new_val
                                    elif key in old:
                                        del old[key]
                                    changed = True
                            if changed:
                                modified_files.add(info["file"])
                                print(f"  ✓ アイテム効果更新: {_eff_label2(old)}")
                                any_change = True
                    if any_change and not value_updated:
                        update_count += 1

                # 画像が未取得なら取得
                if detail.get("image_url") and not _image_already_downloaded(entry.name, norm_image_mapping):
                    img_url = detail["image_url"]
                    ext = img_url.rsplit(".", 1)[-1].lower()
                    if ext not in ("png", "jpg", "jpeg"):
                        ext = "png"
                    img_filename = f"{entry.wiki_id}.{ext}"
                    img_path = IMAGE_DIR / img_filename
                    print(f"  画像: {img_filename}")
                    if not args.dry_run:
                        download_file(img_url, img_path)
                        append_mapping(entry.wiki_id, entry.name, img_filename)
                        img_count += 1

        except Exception as e:
            print(f"  ✗ エラー: {e}")

        if i < len(to_process) - 1:
            time.sleep(args.delay)

    # 8. ファイル保存
    if modified_files and not args.dry_run:
        print("\nファイル保存中...")
        for filepath in modified_files:
            write_yaml_with_flow_values(filepath, all_by_file[filepath])
            print(f"  保存: {filepath}")

    # 9. 更新済み記録を保存
    if not args.dry_run:
        synced_names = load_synced()
        for action, entry in to_process:
            synced_names.add(entry.name)
        save_synced(synced_names)
        print(f"  更新済み記録: {len(synced_names)} 枚")

    print(f"\n=== 完了 ===")
    print(f"  新規追加: {new_count} 枚")
    print(f"  値更新: {update_count} 枚")
    print(f"  画像取得: {img_count} 件")


if __name__ == "__main__":
    main()
