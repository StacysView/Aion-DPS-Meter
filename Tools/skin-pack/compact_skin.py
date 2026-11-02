"""usage: python compact_skin.py <skin folder> <factor> [width] : shrinks the frame of a skin copy (ornaments, overhang, garland, padding) by <factor>."""
import json
import sys

folder, f = sys.argv[1], float(sys.argv[2])
p = f'{folder}/skin.json'
s = json.load(open(p, encoding='utf-8'))
fr = s['frame']
fr['ppp'] = round(fr['ppp'] * f, 5)
fr['overhang'] = {k: round(v * f, 2) for k, v in fr['overhang'].items()}
fr['corner'] = {k: round(v * f, 2) for k, v in fr['corner'].items()}
if 'garland' in s:
    g = s['garland']
    g['bulb'] = round(g['bulb'] * max(f, 0.72), 2)
    g['spacing'] = round(g['spacing'] * max(f, 0.8), 2)
    g['reach'] = round(g['reach'] * f * 0.45, 1)
pad = [18, 14, 18, 22] if s['id'] == 'hiver' else [21, 25, 21, 29]
s['panel'] = {'padding': [round(x * f * 0.8, 1) for x in pad]}
if len(sys.argv) > 3:
    s['panel']['width'] = float(sys.argv[3])
json.dump(s, open(p, 'w', encoding='utf-8'), indent=2)
print(s['id'], fr['ppp'], s['panel'])
