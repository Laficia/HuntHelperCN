LuckyStarMOGAI is derived from NiGuangOwO/LuckyStar, commit 51c2a355a44200eb59c374c8211001c80f4b9d30.
Original project: https://github.com/NiGuangOwO/LuckyStar
Original author: NiGuangOwO. Original license: AGPL-3.0-or-later.
Modified by Laficia on 2026-11-13: independent identity/version; CN farm profiles; Ruby Sea three-cave patrol, normal loading transitions, per-session observation counts, and lifecycle/stop handling.
ECommons 3.2.0.15: https://github.com/NightmareXIV/ECommons (MIT); see THIRD-PARTY-LICENSES/ECommons-MIT.txt.
Source, tests and build instructions accompany this release. No warranty; see LICENSE.md.
2026-11-14: added optional RSR, Wrath lease, and Prome public IPC adapters, selected-target approach and owned-switch lifecycle. No third-party rotation implementation is included.
2026-11-14: added mounted flight between targets and within caves, mounted underwater travel, per-kind observed kill goals, shared cave progress, completed-kind filtering, and in-game completion notifications.
2026-11-14: fixed dismount animation descent falsely invalidating a completed landing and added a bounded native dismount handoff when flight ends just above verified ground; combat approach now respects job attack distance and target hitbox to avoid chasing within range.
2026-11-14: restored the original compact farm rows and header, inserted the Ruby Sea row below Rak'tika, and placed the rotation-provider dropdown beside the first-target delay option. Existing farm behavior and saved selections are retained.
