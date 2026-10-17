# Fish Bloom

Adapted from the local Orbitra export `Bloom-2026-10-17`, based on repository
commit `1e4fe398dabe252bf9a849144e2e643a3cb22c51` with local changes included.
The export's MIT notice is preserved in `LICENSE.TXT` and covers the imported
Bloom renderer, its shaders under `Resources/Textures/_Fish/Shaders`, and the
adapted calculation tests.

The effect samples unshaded sprite layers and existing `TileEmission` sources.
Each sprite has an independent source and occlusion mask in a shared atlas;
the halo is drawn before the normal field-of-view pass. Unsupported sprite
postshaders and active stealth are excluded from both sources and blockers.
The ordinary interaction outline preserves eligibility.

Graphics settings use `fish.bloom_enabled`, `fish.bloom_strength`, and
`fish.bloom_quality`. Low, Medium, and High reduce the blur mask dimensions by
8, 4, and 2 respectively. Full-resolution occlusion preserves object edges.
Disabling the effect or setting strength to zero releases its render resources.

The legacy Sunrise renderer and tree are excluded from discovery by partial
declarations in this directory. Their implementation and archived CVars remain
available for upstream merges. Other Orbitra effects and its settings framework
are not part of this port.
