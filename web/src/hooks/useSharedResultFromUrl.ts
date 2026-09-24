import { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { decodeSharePayload, extractShareToken, type SharePayload } from '../services/shareState';

/**
 * URL ハッシュ (#s=...) の共有トークンを復号し、applyShared でストアへ結果を復元する。
 * トークンが無ければ何もしない。復元できないとき (壊れたリンク・別タブ用のリンク・
 * 現在のデータに無いカード) は利用者向けのメッセージを返す。
 *
 * ページはデータ読込完了後にしか描画されないので、復元時点でカード・プランは揃っている。
 */
export function useSharedResultFromUrl(
  planId: string,
  applyShared: (payload: SharePayload) => string | null,
): string | null {
  const { hash } = useLocation();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const token = extractShareToken(hash);
    if (!token || !planId) return;
    let cancelled = false;
    decodeSharePayload(token).then((payload) => {
      if (cancelled) return;
      if (!payload) {
        setError('共有リンクを読み取れませんでした。リンクが途中で切れているか、このブラウザでは表示できません。');
        return;
      }
      if (payload.plan !== planId) {
        setError('この共有リンクは別のシナリオタブの結果です。リンクのページを開き直してください。');
        return;
      }
      setError(applyShared(payload));
    });
    return () => {
      cancelled = true;
    };
  }, [hash, planId, applyShared]);

  return error;
}
