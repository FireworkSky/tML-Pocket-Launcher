# GLES and Vulkan Benchmark Design

## Goal

Provide a repeatable way to compare the launcher's OpenGL ES and SDL_GPU/Vulkan renderers on the same Android device, then use the measurements to guide targeted FNA3D improvements.

## Current Context

- The launcher builds SDL3 with SDL_GPU and Vulkan enabled.
- FNA3D includes both its OpenGL and SDL_GPU drivers. In SDL3 builds, `FNA3D_FORCE_DRIVER=Vulkan` aliases to the SDL_GPU driver and requests its Vulkan backend.
- The existing FNA3D OpenGL driver includes opt-in runtime diagnostics. The SDL_GPU driver currently lacks matching benchmark-facing reporting.
- Mobile controls are compiled into the APK-owned `MobileLauncher.Controls` assembly. The launcher does not package a built-in tModLoader mod.
- Launcher render scale is configurable and affects the backbuffer, so benchmark runs must record it and keep it constant when comparing backends.

## Proposed Design

### Benchmark Mod

Add a client-only `RendererBench` tModLoader mod that can be enabled independently from the mobile controls mod. It will expose a command and an on-screen control to run a deterministic rendering workload in a controlled test screen or world. Workload levels will exercise sprite throughput, draw-call overhead, and render-target/effect work without depending on a generated world, player equipment, network, or random content.

The run will include warm-up and measurement phases. It will report frame count, elapsed time, average FPS, and frame-time percentiles (p50/p95/p99), plus workload identity, resolution/render scale, selected renderer, device/build identifiers, and whether the run completed. Results will be viewable in-game and exported to a plain structured file for comparing GLES and Vulkan runs. It will not silently change renderer settings; switching backend requires a new game launch.

### Native FNA3D Diagnostics and Optimization

Add matching opt-in diagnostics to the SDL_GPU driver, including the selected SDL_GPU backend/device, draw and upload counts/bytes, CPU command-record/submit/present timings, and frame counts. Keep the existing OpenGL diagnostics and align field names where practical. Do not label CPU timings as GPU execution time. Report GPU duration only if the active SDL_GPU backend exposes a reliable non-blocking timer-query path on Android.

Use the same mod workload and settings to compare the two backends. Make native optimizations only for measured bottlenecks, preserve rendering correctness and resource lifetime rules, and keep diagnostics disabled by default. Avoid enabling global quality or synchronization changes solely because a synthetic benchmark appears faster; any tuning must also pass a tModLoader menu/world smoke test on both renderers.

### Comparison Protocol

Run each backend after a fresh process launch, with matching render scale, resolution, device thermal state, and workload. Discard warm-up samples; run each workload multiple times and compare medians and tail frame times. Include an idle/menu baseline so the synthetic workload's overhead is visible. Record failures and backend fallback as failed runs instead of treating them as valid measurements.

## Approaches Considered

1. **Benchmark mod only:** Smallest change and easy to repeat, but it cannot explain whether time is spent in FNA3D command submission, uploads, or present.
2. **Benchmark mod plus native diagnostics and measured optimization (recommended):** Adds the observability needed to explain differences and validate FNA3D changes, while keeping the workload reproducible.
3. **Compare ordinary gameplay only:** Most representative of user experience, but world content, simulation, camera, and thermal variation make regressions difficult to reproduce or attribute.

## Scope and Non-Goals

- Include a standalone test mod, FNA3D instrumentation for OpenGL ES and SDL_GPU/Vulkan, targeted native optimizations based on measurements, APK build, and device comparison.
- Keep renderer selection and existing plugin renderers intact.
- Do not build a general renderer control API or change user-facing renderer defaults.
- Do not promise GPU-time readings on devices/backends where SDL_GPU cannot provide reliable asynchronous timing.
- Do not optimize unrelated game simulation, launcher UI, or mod loading.

## Validation

- Build and package the benchmark mod using the repository's isolated tModLoader packaging flow.
- Run relevant managed/native tests and build the Android APK.
- On the connected Android device, verify the selected backend in native logs, run each workload on GLES and SDL_GPU/Vulkan, export results, and compare repeated runs at matching settings.
- Verify that both backends still reach the tModLoader main menu and render a world after native changes.

## Open Constraints

- GPU timer-query availability is backend and driver dependent; the first implementation may report CPU frame and submit/present timings only.
- Native optimizations will be selected after the baseline data is collected; no fixed optimization is assumed in advance.
