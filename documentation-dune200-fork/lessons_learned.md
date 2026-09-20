# Post-Mortem & Lessons Learned: The Terrain Distortion Bug

**Date**: September 20, 2026  
**Incident**: Desert terrain in Dune 2000 missions rendered with repeating horizontal cliff/ridge stripes across flat sand.  
**Resolution Commit**: `00621b01b8` (*Fix: eliminate terrain distortion by removing vertex buffer orphaning and restoring memory zeroing*)  
**Impact**: High token and diagnostic time consumption due to visual misdirection.

---

## 1. Executive Summary

During testing of Dune 2000 campaign missions (specifically Harkonnen Mission 1b), the in-game battlefield rendered with severe graphical glitches: flat desert sand was corrupted by repeating horizontal bands of rocky cliff edges and ridges. 

Initial appearances strongly suggested corrupt campaign map binary files, broken tileset yaml configs (`arrakis.yaml`), or distorted `BLOXBASE.R16` asset extraction. However, deep offline rendering verified that all map data and assets were 100% healthy.

The actual root cause was a **destructive OpenGL buffer orphaning bug** introduced in our fork's low-level graphics layer ([`OpenRA.Platforms.Android/VertexBuffer.cs`](../OpenRA.Platforms.Android/VertexBuffer.cs)).

---

## 2. Root Cause Analysis

### A. The Flawed Optimization
In commit `d73f2064fe`, in an attempt to prevent GPU pipeline stalls during high-frequency batch rendering, buffer orphaning was added to `VertexBuffer.SetData()`:

```csharp
// FLAWED CODE in OpenRA.Platforms.Android/VertexBuffer.cs
public void SetData(T[] data, int offset, int start, int length)
{
    if (length <= 0)
        return;

    Bind();

    // The Fatal Assumption: "start == 0 means reset buffer for a fresh batch"
    if (start == 0 && bufferSize > 0)
    {
        OpenGL.glBufferData(OpenGL.GL_ARRAY_BUFFER,
            new IntPtr(VertexSize * bufferSize),
            IntPtr.Zero,
            OpenGL.GL_DYNAMIC_DRAW);
    }

    unsafe
    {
        fixed (T* ptr = &data[offset])
        {
            OpenGL.glBufferSubData(OpenGL.GL_ARRAY_BUFFER,
                new IntPtr(VertexSize * start),
                new IntPtr(VertexSize * length),
                new IntPtr(ptr));
        }
    }
}
```

### B. The Architectural Conflict: Transient vs Persistent Buffers
The developer assumed `VertexBuffer<T>` is only used for `tempVertexBuffer` (transient per-frame quad batches flushed from index 0).

**This assumption was false.** OpenRA uses the same `IVertexBuffer<T>` interface for two completely different workloads:
1. **Transient Batch Buffers** (`tempVertexBuffer` in `Renderer.cs`): Cleared and overwritten multiple times per frame.
2. **Persistent Geometry Buffers** ([`TerrainSpriteLayer.cs`](../OpenRA.Game/Graphics/TerrainSpriteLayer.cs)): Allocated **once** per map (`4 * MapWidth * MapHeight` vertices; 3,584 vertices for a 32×28 map) and kept persistently in GPU VRAM.

### C. How the Glitch Triggered
In `TerrainSpriteLayer.Draw()`, terrain tile changes are flushed to the GPU incrementally row-by-row:
```csharp
for (var row = firstRow; row <= lastRow; row++)
{
    if (!dirtyRows.Remove(row)) continue;
    var rowOffset = vertexRowStride * row;
    vertexBuffer.SetData(vertices, rowOffset, rowOffset, vertexRowStride);
}
```

1. Whenever **row 0** was dirty (on map load, shroud reveal, or top-edge scrolling), `rowOffset == 0` (`start == 0`).
2. The check `if (start == 0 && bufferSize > 0)` evaluated to **true**.
3. `glBufferData(..., IntPtr.Zero)` executed, commanding the GPU driver to **orphan and completely discard the entire 3,584-vertex map buffer**.
4. The driver allocated a new uninitialized VRAM memory block from its internal recycle pool.
5. `glBufferSubData` then wrote **only row 0's 128 vertices**.
6. Rows 1 to 27 were **not** re-uploaded if they were not dirty in that frame.
7. `TerrainSpriteLayer` then issued `DrawVertexBuffer` across the visible rows. Rows 1 through 27 were drawn directly from **uninitialized/recycled GPU driver memory**.
8. In mobile GPUs (such as Qualcomm Adreno), recycled buffer memory contains leftover vertex data from previous draw batches (cliff tiles, sidebar frames, radar sprites). This resulted in distinct horizontal stripes of random cliff textures repeating across the screen.

---

## 3. Why It Cost Substantial Tokens & Time

Visual bugs in game engines often look like asset bugs:
1. **Visual Misdirection**: The glitch rendered perfectly recognizable Dune 2000 cliff sprites. This created a strong initial hypothesis that:
   - The map binary (`map.bin`) had invalid tile indices.
   - The tileset mapping table in `arrakis.yaml` had incorrect frame offsets.
   - The asset extractor had corrupted `BLOXBASE.R16` during decompression.
2. **Exhaustive Diagnostic Path**:
   - Parsed binary map structures and decoded 896 cell tile records.
   - Extracted official `BLOXBASE.R16` from `d2k-quickinstall-v3.zip`.
   - Rendered all 13 frames of `Template@0` and confirmed they were 100% clean sand.
   - Built a standalone Python rendering pipeline to generate an exact offline map preview (`harkonnen01b_rendered_exact.png`).
3. **The Pivot**:
   - Offline rendering proved that the game assets, palettes, and map data were immaculate.
   - Only then did focus shift to the GPU upload path, where commit logs quickly exposed the `start == 0` buffer orphaning bug.

---

## 4. The Fix

1. **Remove Destructive Buffer Orphaning**: Completely eliminated `glBufferData(..., IntPtr.Zero)` from `SetData()`. Partial row updates now safely modify only their target sub-ranges via `glBufferSubData` without wiping adjacent rows.
2. **Restore Buffer Memory Zeroing**: Upstream OpenRA zeroed buffer memory in `VertexBuffer(int size)`. When buffer orphaning was added, this zeroing had been removed. We restored memory zeroing using zero-allocation C# `unsafe fixed` stack pointers to guarantee unwritten buffer regions never contain driver garbage.
3. **Preserve Fast Unsafe Pinning**: Retained zero-allocation `unsafe fixed` stack pinning for all `glBufferSubData` calls, keeping GC allocations at absolute zero.

---

## 5. Golden Rules for Future Development

To prevent similar regressions and avoid wasting tokens/time:

### Rule 1: Never Add Global Side-Effects to Polymorphic Interfaces
`IVertexBuffer<T>` is a shared interface implemented by platform backends. Never assume how callers use an interface based on a single caller (`tempVertexBuffer`). If a method accepts `start` and `length`, it is a **partial update** contract—never mutate or discard elements outside `[start .. start + length]`.

### Rule 2: Respect Upstream OpenRA Platform Implementations
Upstream OpenRA's desktop backend ([`OpenRA.Platforms.Default/VertexBuffer.cs`](../OpenRA.Platforms.Default/VertexBuffer.cs)) has been battle-tested on Windows, Linux, macOS, and dozens of GPU drivers for over a decade. It intentionally:
- Does **not** orphan buffers in `SetData()`.
- **Zeroes** buffer storage upon allocation.  
Before modifying platform rendering primitives, inspect how upstream implemented them and ask *why* they chose that design.

### Rule 3: When Visual Bugs Occur, Audit Recent Platform Commits First
If a visual glitch appears in content that is known to be standard/official (like campaign mission 1):
- Run `git log -p -n 10 OpenRA.Platforms.Android/` before analyzing map binaries or game assets.
- Asset files rarely corrupt themselves; platform wrapper code and shader/buffer logic are almost always the culprit.

### Rule 4: Verify Full Buffer Lifecycle in Graphics Code
Whenever modifying OpenGL buffer management (`glBufferData`, `glBufferSubData`, buffer mapping):
- Verify where the buffer is created.
- Verify whether the buffer is persistent (lifespan of the map/game) or transient (lifespan of a single draw call).
- Verify whether updates are partial (`length < size`) or complete (`length == size`).
