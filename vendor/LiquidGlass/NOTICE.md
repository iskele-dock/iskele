# Liquid Glass renderer attribution

Upstream: https://github.com/PedroRuedas/Windows-Liquid-glass
Revision: cc657382355c4398acf9a9a975fdad836ecd5639
License: GPL-3.0; the full upstream LICENSE is retained beside this file.

The selected Controls, Effects, Graphics, Interop and Services files retain their upstream authorship.

Iskele integration changes (2026-11-18): a .NET 9 project with Windows 22621 API metadata and Windows 19041 minimum; app-owned data/log paths in IskeleServices.cs; bounded shutdown wait in LiveBackdrop.SetEnabled. Iskele's wrapper, controls, tests and UI adaptations are marked [Iskele].

Additional performance change: LiquidGlassSurface/Effect and the HLSL shader accept ShapeCenter/ShapeSize uniforms. The framebuffer and capture region keep a stable size while the balloon morphs. Shader bytecode was rebuilt with the Windows D3DCompiler 47, entry main, ps_3_0, optimization level 3. Zero ShapeSize preserves upstream full-surface behavior.

The original repository's complete application, shell integrations, installer, marketing assets, FX package and GLSL sample are not included in this integration.
