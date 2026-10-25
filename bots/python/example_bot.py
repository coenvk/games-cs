#!/usr/bin/env python3
"""An example poker bot in Python: standard library only, Python 3.8 or newer.

It speaks the JSON-lines protocol of docs/poker-bots.md: one JSON object per line on stdin and stdout. Only protocol
lines may go to stdout; print anything else for debugging to stderr (print(..., file=sys.stderr)).

The play is simple: raise with a pair or two high cards, otherwise check, call when the price is small, and fold.
"""
import json
import sys

PROTOCOL = 1
NAME = "Python example"
RANKS = "23456789TJQKA"


def send(message):
    """Writes one protocol line and flushes it, or the app never sees it."""
    sys.stdout.write(json.dumps(message, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def rank(card):
    return RANKS.index(card[0])


def decide(d):
    """Returns the action for a decision. 'legal' says what is allowed; anything else counts as a fold."""
    legal = d["legal"]
    low, high = sorted(rank(c) for c in d["hole"])
    strong = high == low or low >= RANKS.index("T")
    if strong and legal.get("minRaiseTo") is not None:
        # A raise is "raise to": the total bet for this street, within the legal range.
        target = max(legal["minRaiseTo"], min(3 * d["bigBlind"], legal["maxRaiseTo"]))
        return {"type": "action", "action": "raise", "to": target}
    if legal["check"]:
        return {"type": "action", "action": "check"}
    if d["toCall"] <= d["bigBlind"]:
        return {"type": "action", "action": "call"}
    return {"type": "action", "action": "fold"}


def main():
    for line in sys.stdin:
        try:
            message = json.loads(line)
        except ValueError:
            continue  # Not JSON: ignore it.
        if not isinstance(message, dict):
            continue
        kind = message.get("type")
        if kind == "hello":
            send({"type": "hello", "protocol": PROTOCOL, "name": NAME})
        elif kind == "decision":
            answer = decide(message)
            answer["id"] = message["id"]  # Echo the id so a late answer is never taken for the next decision.
            send(answer)
        elif kind == "match-end":
            break
        # match-start, hand-start, hand-end and any message type not known yet: nothing to do.


if __name__ == "__main__":
    main()
