# Product

## Register

product

## Users

Developers and technical operators running a local MCP bridge who need to inspect exchange state, configure conservative limits, review trading plans, and explicitly control Demo or Live execution without surrendering authority to an AI client.

## Product Purpose

TradeRelay makes local exchange access observable and controllable. Success means users can understand the current safety posture, validate risk, review exactly what an MCP client proposes, and trace every explicitly enabled Demo or Live write through reconciliation.

Version 1.1 turns the product into a Live-oriented operator console without expanding the write boundary. The persistent Operations Rail keeps provider, environment, health, audit, and trading state visible while a provider registry prepares the product for concurrent read-only adapters.

Version 1.2 makes exchange operations observable: current positions, orders, fills, protection, exchange-returned history, TradeRelay-observed lifecycle events, reconciliation, and grouped safe runtime errors remain explicitly sourced and reviewable.

Version 1.3 pairs local agent clients through scoped OAuth and installs one canonical operator skill across Codex, Claude Code, and Gemini CLI. Read & Plan is the default; Trade is a deliberate re-pairing decision and still cannot enable trading or bypass desktop approvals, audit, gates, or reconciliation.

## Brand Personality

Calm, precise, protective. The product should communicate expert confidence without trading hype, alarm fatigue, or decorative complexity.

## Anti-references

- Neon trading terminals that use visual intensity as a substitute for hierarchy.
- Consumer brokerage apps that hide technical constraints behind simplified marketing language.
- Generic card-grid dashboards with weak information density and ambiguous status colors.
- Interfaces that imply simulated, approved, and executed are interchangeable states.

## Design Principles

1. Make safety state legible before exposing an action.
2. Show normalized values and assumptions instead of hiding exchange constraints.
3. Keep human decisions visibly separate from MCP requests.
4. Prefer familiar desktop controls and compact information hierarchy over novelty.
5. State uncertainty explicitly; never manufacture precision for unknown risk.
6. Diagnostics help operators without exporting secrets, raw audit entries, or authenticated payloads.

## Accessibility & Inclusion

Meet WCAG 2.2 AA for contrast, keyboard access, focus visibility, target sizing, and non-color status communication. Respect reduced-motion preferences and avoid decorative animation.
