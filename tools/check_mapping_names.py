import yaml, sys
sys.stdout.reconfigure(encoding='utf-8')

# Load mapping
mapping_names = {}
for line in open('Data/Images/_mapping.tsv', encoding='utf-8'):
    if line.startswith('card_id'): continue
    parts = line.strip().split('\t')
    if len(parts) >= 3 and parts[2] != 'ERROR':
        mapping_names[parts[1].strip()] = parts[2].strip()

# Check what the mapping has for these cards
targets = ['みいつけた', 'オシャレもメイク', '貸せ、手本', 'ぜったいに取る', '盛り上げて',
           'お母さんか', '仲良し', 'もう一戦', 'プロデュースって', '欠ける事なく',
           '教室パーティー', '仲良しって感じ', '迷子', 'みんなの意見', 'さみだれ',
           'めんどくさ', 'やればできる', '仲直り', '仕事のつもり',
           'バレンタイン', 'ガールズ']

for name, fn in mapping_names.items():
    if any(t in name for t in targets):
        print(f'  TSV: "{name}" -> {fn}')
