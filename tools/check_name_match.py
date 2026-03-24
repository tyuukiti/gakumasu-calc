import yaml, sys
sys.stdout.reconfigure(encoding='utf-8')

# Load mapping (card_name -> filename)
mapping = {}
norm_mapping = {}

def normalize(name):
    return name.replace("～","~").replace("〜","~").replace("！","!").replace("？","?").replace("♡","").replace("♪","").replace("☆","").replace("★","").replace("\u3000"," ").strip()

for line in open('Data/Images/_mapping.tsv', encoding='utf-8'):
    if line.startswith('card_id'): continue
    parts = line.strip().split('\t')
    if len(parts) >= 3 and parts[2] != 'ERROR':
        mapping[parts[1].strip()] = parts[2].strip()
        norm_mapping[normalize(parts[1].strip())] = parts[2].strip()

print(f"Mapping entries: {len(mapping)}")
print()

for fn in ['Data/SupportCards/ssr_cards.yaml', 'Data/SupportCards/sr_cards.yaml', 'Data/SupportCards/r_cards.yaml']:
    data = yaml.safe_load(open(fn, encoding='utf-8'))
    for c in data['support_cards']:
        name = c['name']
        if name in mapping:
            continue
        if normalize(name) in norm_mapping:
            continue
        print(f'NO MATCH: [{c["rarity"]}] {c["id"]} "{name}"')
