import yaml, sys
sys.stdout.reconfigure(encoding='utf-8')

targets = ['やっと見つけたぞ', 'ふわふわでワクワク', 'おっと、危ないよ', 'いつまでも続けばいいのに', 'そろそろ焼けたかな', 'ちょっと詳しいんです', 'もうっ', 'SSDのひみつ']

for fn in ['Data/SupportCards/ssr_cards.yaml', 'Data/SupportCards/sr_cards.yaml', 'Data/SupportCards/r_cards.yaml']:
    with open(fn, encoding='utf-8') as f:
        data = yaml.safe_load(f)
    for card in data['support_cards']:
        if any(t in card['name'] for t in targets):
            sp_effects = [e for e in card['effects'] if e.get('value_type') == 'sp_rate']
            flat_total = sum(e['value'] for e in card['effects'] if e.get('value_type') == 'flat')
            para_bonus = sum(e['value'] for e in card['effects'] if e.get('value_type') == 'para_bonus')
            sp_rate = sp_effects[0]['value'] if sp_effects else 0
            print(f"[{card['rarity']}] {card['id']} {card['name']}  type={card['type']} plan={card.get('plan','')}  sp_rate={sp_rate}  flat_total={flat_total}  para_bonus={para_bonus}")
            for e in card['effects']:
                print(f"   {e}")
            print()

print("=== ALL SP_RATE CARDS (Da/Vi) ===")
for fn in ['Data/SupportCards/ssr_cards.yaml', 'Data/SupportCards/sr_cards.yaml', 'Data/SupportCards/r_cards.yaml']:
    with open(fn, encoding='utf-8') as f:
        data = yaml.safe_load(f)
    for card in data['support_cards']:
        if card['type'] in ('da', 'vi'):
            sp_effects = [e for e in card['effects'] if e.get('value_type') == 'sp_rate']
            if sp_effects:
                flat_total = sum(e['value'] for e in card['effects'] if e.get('value_type') == 'flat')
                para_bonus = sum(e['value'] for e in card['effects'] if e.get('value_type') == 'para_bonus')
                sp_rate = sp_effects[0]['value'] if sp_effects else 0
                print(f"[{card['rarity']}] {card['id']} {card['name']}  type={card['type']}  sp_rate={sp_rate}  flat_total={flat_total}  para_bonus={para_bonus}")
