# Design System

## Theme

Dark desktop product UI designed for sustained use in typical developer-workstation lighting. The surface is restrained and low-glare; cyan is reserved for focus, selection, and primary action. Semantic colors always appear with text labels.

## Color Roles

- App background: `#0B1118`
- Navigation/shell: `#101923`
- Raised surface: `#16212C`
- Secondary surface: `#1B2835`
- Border: `#334454`
- Primary ink: `#F2F6F8`
- Secondary ink: `#B8C6D2`
- Accent/focus: `#35C9E8`
- Accent active: `#63D8EE`
- Success: `#57D6A5`
- Warning: `#F0CA67`
- Error: `#F08A93`

## Typography

Use the platform sans-serif throughout the product. Keep the scale compact: 12px metadata, 14px body and controls, 16px subsection labels, 20px section titles, and 24px page/status titles. Use a platform monospace only for tokens, IDs, hashes, and endpoints.

## Layout

- Persistent health/status strip and an expanded Operations Rail at 1080×720 that compacts to accessible abbreviated labels at 900×640.
- Content margin: 24–32px; section gap: 20–24px; control gap: 8–12px.
- Default window: 1080×720; minimum supported window: 900×640.
- Use split list/detail layouts for review workflows and grouped forms for settings.
- Use a reverse-chronological Activity list with inline technical details for execution and audit history.
- Keep exchange-returned history and TradeRelay-observed lifecycle history visibly labeled; never imply that observation reconstructs earlier account activity.
- Operations uses compact tabular rows with textual Long/Short direction, while Error Center groups stable safe codes with recovery guidance.
- Use a focused Settings form for MCP lifecycle, application paths, diagnostics, and release identity.
- Avoid nested cards; borders define only genuine panels or grouped workflows.

## Components

- Buttons and inputs use an 8px radius and at least 44px height.
- Primary action uses cyan with dark text; destructive action uses an error tint and explicit label.
- Every interactive control has default, hover, focus, disabled, and loading states.
- Focus uses a clearly visible 2px cyan outline.
- Status badges include text and restrained semantic backgrounds; color is never the sole signal.
- Live selection always displays a persistent textual red indicator with the current enabled or disabled state.
- Live enablement uses one focused confirmation dialog with the complete risk summary and an exact typed phrase.
- Destructive Live action confirmations share the compact Approvals list/detail pattern rather than introducing another page.
- Validation messages sit directly below the affected group and use actionable language.
- Production loading, empty, and failure states explain the next safe operator action and never disappear silently.

## Motion

Use no decorative motion. State transitions may crossfade within 150–200ms when supported, and must remain understandable with reduced motion or no animation.

## Voice

Use short, factual labels. Distinguish Requested, Normalized, Estimated, Approved, Expired, and Executed precisely. Never describe estimated risk as guaranteed loss or an approved simulation as executable.
