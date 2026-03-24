import yaml, sys
sys.stdout.reconfigure(encoding='utf-8')

for fn in ['Data/SupportCards/ssr_cards.yaml', 'Data/SupportCards/sr_cards.yaml']:
    data = yaml.safe_load(open(fn, encoding='utf-8'))
    for c in data['support_cards']:
        if c['type'] == 'vi' and any(e.get('value_type') == 'sp_rate' for e in c['effects']):
            flat = sum(e['value'] for e in c['effects'] if e.get('value_type') == 'flat')
            sp = [e['value'] for e in c['effects'] if e.get('value_type') == 'sp_rate'][0]
            print(f"[{c['rarity']}] {c['id']} {c['name']}  plan={c.get('plan','')}  sp_rate={sp}  flat={flat}")
