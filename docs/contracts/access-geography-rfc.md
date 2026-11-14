# Access geography: gated contract proposal (GEO-01)

**Status: PROPOSED, no endpoint, no map data.** The user confirmed that an approved location source and retention policy are not yet available. The existing `GET /api/v1/security/access-events` remains Admin-only and exposes audit time, outcome and network IP only. It does not establish location or online presence.

An eventual read API may return **aggregated, approximate city/region counts** and a matching accessible list from the same authorized snapshot. It must never return per-person coordinates or convert an IP to GPS. Unknown, private, proxy and VPN addresses must remain unplotted and counted as unknown. An online count must be omitted or null until a separately authorized session/presence source exists.

Before implementing this API, decide and document: purpose, permitted Admin and Department Staff audience; current-versus-historical department transfer semantics; approved licensed source and refresh process; city-level accuracy statement; retention and deletion; minimum aggregation threshold; audit of access to the overview; and tile-provider privacy/licensing. Department Staff scope must be derived from persisted department membership on the server and negative-tested against other departments. No FE map or marker should be enabled before these gates are met.
