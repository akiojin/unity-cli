#!/usr/bin/env python3
"""Summarize an assertion eval output: summ.py raw/<label>.out"""
import json, sys
d = json.load(open(sys.argv[1]))
r = d.get("value", d.get("result", d))
print("state:", d.get("state"), "| total:", r.get("total") if isinstance(r, dict) else None, "| failed:", r.get("failed") if isinstance(r, dict) else None)
if isinstance(r, dict):
    for c in r.get("checks", []):
        if not c["pass"]:
            print("FAIL", c)
    print(json.dumps({k: v for k, v in r.items() if k != "checks"}))
else:
    print(json.dumps(d)[:2000])
