"""Reads bench-results.log and prints Markdown tables: timings per step and variant, parity differences per type."""
import sys
from collections import defaultdict

log = open(sys.argv[1], encoding='utf-8', errors='replace').read().splitlines()
timing = defaultdict(dict)   # step -> variant -> (median, best, records, checksum)
parity = defaultdict(dict)   # type -> variant -> count
selfeq = defaultdict(dict)
variants = []
for line in log:
    parts = line.split('|')
    if parts[0] == 'RESULT' and len(parts) == 7:
        _, v, step, med, best, recs, chk = parts
        timing[step][v] = (int(med), int(best), int(recs), int(chk))
        if v not in variants: variants.append(v)
    elif parts[0] in ('PARITY', 'SELF') and len(parts) == 4:
        _, v, t, n = parts
        (parity if parts[0] == 'PARITY' else selfeq)[t][v] = int(n)
        if v not in variants: variants.append(v)

wanted = sys.argv[2].split(',') if len(sys.argv) > 2 else variants

def table(title, rows, fmt):
    print(f'\n### {title}\n')
    print('| | ' + ' | '.join(wanted) + ' |')
    print('| --- |' + ' --- |' * len(wanted))
    for name, cells in rows:
        print(f'| {name} | ' + ' | '.join(fmt(cells.get(v)) for v in wanted) + ' |')

table('Timings (median of 7 runs, ms; best in brackets)', sorted(timing.items(), key=lambda kv: list(timing).index(kv[0])),
      lambda c: '' if c is None else f'{c[0]:,} ({c[1]:,})')
table('Records differing between CreateFromBinary and the overlay', sorted(parity.items()), lambda c: '0' if c is None else f'{c:,}')
table('Records unequal to a second CreateFromBinary of the same bytes', sorted(selfeq.items()), lambda c: '0' if c is None else f'{c:,}')
